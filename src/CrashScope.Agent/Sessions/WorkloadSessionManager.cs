using CrashScope.Agent.Incidents;
using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Core.Incidents;
using CrashScope.Core.Sessions;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Sessions;

internal sealed record SessionAttachResult(WorkloadSession? Session, string? Detail)
{
    public bool IsAttached => Session is not null;
}

internal sealed class WorkloadSessionManager : ITelemetryFrameSink
{
    private static readonly TimeSpan IncidentAssociationGrace = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UnavailableExitWaitFallbackInterval = TimeSpan.FromSeconds(5);

    private readonly MonitoredProcessTracker _processTracker;
    private readonly SamplingModeController _samplingMode;
    private readonly IWorkloadSessionRepository _repository;
    private readonly ISamplingClock _clock;
    private readonly IProcessExitWaiter _processExitWaiter;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _activeSessionSignal = new(0, 1);
    private readonly object _sync = new();
    private readonly List<WorkloadSession> _sessions = new();
    private WorkloadSession? _activeSession;
    private ProcessInstance? _activeProcess;
    private CancellationTokenSource? _activeExitWaitCancellation;

    public WorkloadSessionManager(
        MonitoredProcessTracker processTracker,
        SamplingModeController samplingMode,
        IWorkloadSessionRepository repository,
        ISamplingClock clock)
        : this(
            processTracker,
            samplingMode,
            repository,
            clock,
            new SystemProcessExitWaiter())
    {
    }

    internal WorkloadSessionManager(
        MonitoredProcessTracker processTracker,
        SamplingModeController samplingMode,
        IWorkloadSessionRepository repository,
        ISamplingClock clock,
        IProcessExitWaiter processExitWaiter)
    {
        ArgumentNullException.ThrowIfNull(processTracker);
        ArgumentNullException.ThrowIfNull(samplingMode);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(processExitWaiter);

        _processTracker = processTracker;
        _samplingMode = samplingMode;
        _repository = repository;
        _clock = clock;
        _processExitWaiter = processExitWaiter;
    }

    public WorkloadSession? ActiveSnapshot()
    {
        lock (_sync)
        {
            return _activeSession;
        }
    }

    public IReadOnlyList<WorkloadSession> Snapshot()
    {
        lock (_sync)
        {
            return _sessions.OrderBy(x => x.StartedAtUtc).ToArray();
        }
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var loaded = await _repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var now = _clock.GetUtcNow().ToUniversalTime();
            var reconciled = new List<WorkloadSession>(loaded.Count);

            foreach (var session in loaded)
            {
                if (!session.IsActive)
                {
                    reconciled.Add(session);
                    continue;
                }

                var closed = EndSession(session, now, SessionEndReason.InterruptedOnRestart);
                await _repository.SaveAsync(closed, cancellationToken).ConfigureAwait(false);
                reconciled.Add(closed);
            }

            CancellationTokenSource? oldExitWait;
            lock (_sync)
            {
                _sessions.Clear();
                _sessions.AddRange(reconciled);
                _activeSession = null;
                _activeProcess = null;
                oldExitWait = _activeExitWaitCancellation;
                _activeExitWaitCancellation = null;
            }

            CancelAndDispose(oldExitWait);
            _samplingMode.SetMode(SamplingMode.Background);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<SessionAttachResult> AttachAsync(
        int processId,
        CancellationToken cancellationToken = default) =>
        AttachCoreAsync(processId, null, cancellationToken);

    public ValueTask<SessionAttachResult> AttachCandidateAsync(
        int processId,
        DateTimeOffset expectedProcessStartTimeUtc,
        CancellationToken cancellationToken = default)
    {
        if (expectedProcessStartTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Expected process start time must be UTC.",
                nameof(expectedProcessStartTimeUtc));
        }

        return AttachCoreAsync(processId, expectedProcessStartTimeUtc, cancellationToken);
    }

    private async ValueTask<SessionAttachResult> AttachCoreAsync(
        int processId,
        DateTimeOffset? expectedProcessStartTimeUtc,
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_activeSession is not null)
                {
                    return new SessionAttachResult(
                        null,
                        $"Session {_activeSession.SessionId} is already active for {_activeSession.ProcessName}.");
                }
            }

            var attach = await _processTracker.AttachAsync(processId, cancellationToken)
                .ConfigureAwait(false);
            if (!attach.IsAttached)
            {
                return new SessionAttachResult(null, attach.Detail ?? "Process could not be attached.");
            }

            var process = attach.Instance!;
            if (expectedProcessStartTimeUtc.HasValue &&
                process.Identity.StartTimeUtc != expectedProcessStartTimeUtc.Value)
            {
                return new SessionAttachResult(
                    null,
                    "The selected process identity changed before attach. Refresh workload discovery and select it again.");
            }

            var session = new WorkloadSession(
                Guid.NewGuid(),
                process.Identity.ProcessId,
                process.Identity.StartTimeUtc,
                process.Name,
                process.ExecutablePath,
                _clock.GetUtcNow().ToUniversalTime(),
                null,
                null,
                SessionTelemetrySummary.Empty);

            await _repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            var exitWaitCancellation = new CancellationTokenSource();
            lock (_sync)
            {
                _sessions.Add(session);
                _activeSession = session;
                _activeProcess = process;
                _activeExitWaitCancellation = exitWaitCancellation;
            }

            SignalActiveSession();
            _samplingMode.SetMode(SamplingMode.Active);
            return new SessionAttachResult(session, null);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<WorkloadSession?> StopAsync(
        CancellationToken cancellationToken = default) =>
        await CompleteActiveAsync(
                SessionEndReason.UserStopped,
                cancellationToken,
                expectedProcess: null)
            .ConfigureAwait(false);

    public async ValueTask<ProcessObservation?> ObserveActiveProcessAsync(
        CancellationToken cancellationToken = default)
    {
        ProcessInstance? target;
        lock (_sync)
        {
            target = _activeProcess;
        }

        if (target is null)
        {
            return null;
        }

        var observation = await _processTracker.ObserveAsync(target, cancellationToken)
            .ConfigureAwait(false);

        if (observation.State == ProcessObservationState.Exited)
        {
            await CompleteActiveAsync(
                    SessionEndReason.ProcessExited,
                    cancellationToken,
                    target)
                .ConfigureAwait(false);
        }
        else if (observation.State == ProcessObservationState.PidReused)
        {
            await CompleteActiveAsync(
                    SessionEndReason.PidReused,
                    cancellationToken,
                    target)
                .ConfigureAwait(false);
        }

        return observation;
    }

    /// <summary>
    /// Keeps exactly one asynchronous wait on the currently monitored process handle.
    /// No periodic PID polling occurs during the normal supported path.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProcessInstance? target;
            CancellationTokenSource? sessionExitCancellation;
            lock (_sync)
            {
                target = _activeProcess;
                sessionExitCancellation = _activeExitWaitCancellation;
            }

            if (target is null || sessionExitCancellation is null)
            {
                await _activeSessionSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                sessionExitCancellation.Token);

            ProcessExitWaitResult waitResult;
            try
            {
                waitResult = await _processExitWaiter
                    .WaitForExitAsync(target, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (sessionExitCancellation.IsCancellationRequested &&
                      !cancellationToken.IsCancellationRequested)
            {
                // The user stopped this session or another lifecycle transition replaced it.
                continue;
            }

            switch (waitResult.State)
            {
                case ProcessExitWaitState.Exited:
                case ProcessExitWaitState.NotFound:
                    await CompleteActiveAsync(
                            SessionEndReason.ProcessExited,
                            cancellationToken,
                            target)
                        .ConfigureAwait(false);
                    break;

                case ProcessExitWaitState.PidReused:
                    await CompleteActiveAsync(
                            SessionEndReason.PidReused,
                            cancellationToken,
                            target)
                        .ConfigureAwait(false);
                    break;

                case ProcessExitWaitState.Unavailable:
                    await ReconcileUnavailableExitWaitAsync(target, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public ValueTask WriteAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_activeSession is null)
            {
                return ValueTask.CompletedTask;
            }

            var updated = ReplaceTelemetry(
                _activeSession,
                Accumulate(_activeSession.Telemetry, frame));
            ReplaceCachedSession(updated);
            _activeSession = updated;
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<bool> AssociateIncidentAsync(
        IncidentReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkloadSession? candidate;
            lock (_sync)
            {
                candidate = FindSessionForIncident(report);
            }

            if (candidate is null || candidate.IncidentIds.Contains(report.IncidentId))
            {
                return false;
            }

            var updated = ReplaceIncidentIds(
                candidate,
                candidate.IncidentIds.Append(report.IncidentId).Distinct().ToArray());
            await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                ReplaceCachedSession(updated);
                if (_activeSession?.SessionId == updated.SessionId)
                {
                    _activeSession = updated;
                }
            }

            return true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask ReconcileUnavailableExitWaitAsync(
        ProcessInstance target,
        CancellationToken cancellationToken)
    {
        var observation = await _processTracker.ObserveAsync(target, cancellationToken)
            .ConfigureAwait(false);

        if (observation.State == ProcessObservationState.Exited)
        {
            await CompleteActiveAsync(
                    SessionEndReason.ProcessExited,
                    cancellationToken,
                    target)
                .ConfigureAwait(false);
            return;
        }

        if (observation.State == ProcessObservationState.PidReused)
        {
            await CompleteActiveAsync(
                    SessionEndReason.PidReused,
                    cancellationToken,
                    target)
                .ConfigureAwait(false);
            return;
        }

        // Only unsupported/restricted processes take this low-frequency fallback.
        await _clock.DelayAsync(UnavailableExitWaitFallbackInterval, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<WorkloadSession?> CompleteActiveAsync(
        SessionEndReason reason,
        CancellationToken cancellationToken,
        ProcessInstance? expectedProcess)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkloadSession? current;
            ProcessInstance? currentProcess;
            lock (_sync)
            {
                current = _activeSession;
                currentProcess = _activeProcess;
            }

            if (current is null)
            {
                return null;
            }

            if (expectedProcess is not null &&
                currentProcess?.Identity != expectedProcess.Identity)
            {
                return null;
            }

            var ended = EndSession(
                current,
                _clock.GetUtcNow().ToUniversalTime(),
                reason);
            await _repository.SaveAsync(ended, cancellationToken).ConfigureAwait(false);

            CancellationTokenSource? exitWaitCancellation;
            lock (_sync)
            {
                ReplaceCachedSession(ended);
                _activeSession = null;
                _activeProcess = null;
                exitWaitCancellation = _activeExitWaitCancellation;
                _activeExitWaitCancellation = null;
            }

            CancelAndDispose(exitWaitCancellation);
            _samplingMode.SetMode(SamplingMode.Background);
            return ended;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void SignalActiveSession()
    {
        if (_activeSessionSignal.CurrentCount == 0)
        {
            _activeSessionSignal.Release();
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        finally
        {
            source.Dispose();
        }
    }

    private WorkloadSession? FindSessionForIncident(IncidentReport report)
    {
        if (report.Process is not null)
        {
            return _sessions
                .Where(session =>
                    session.ProcessId == report.Process.ProcessId &&
                    session.ProcessStartTimeUtc == report.Process.StartTimeUtc &&
                    ContainsIncidentTime(session, report.IncidentTimeUtc))
                .OrderByDescending(session => session.StartedAtUtc)
                .FirstOrDefault();
        }

        var candidates = _sessions
            .Where(session => ContainsIncidentTime(session, report.IncidentTimeUtc, IncidentAssociationGrace))
            .OrderByDescending(session => session.StartedAtUtc)
            .Take(2)
            .ToArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool ContainsIncidentTime(
        WorkloadSession session,
        DateTimeOffset incidentTimeUtc,
        TimeSpan? endGrace = null)
    {
        if (incidentTimeUtc < session.StartedAtUtc)
        {
            return false;
        }

        if (!session.EndedAtUtc.HasValue)
        {
            return true;
        }

        return incidentTimeUtc <= session.EndedAtUtc.Value + (endGrace ?? TimeSpan.Zero);
    }

    private static WorkloadSession EndSession(
        WorkloadSession session,
        DateTimeOffset endedAtUtc,
        SessionEndReason reason) =>
        new(
            session.SessionId,
            session.ProcessId,
            session.ProcessStartTimeUtc,
            session.ProcessName,
            session.ExecutablePath,
            session.StartedAtUtc,
            endedAtUtc < session.StartedAtUtc ? session.StartedAtUtc : endedAtUtc,
            reason,
            session.Telemetry,
            session.IncidentIds);

    private static WorkloadSession ReplaceTelemetry(
        WorkloadSession session,
        SessionTelemetrySummary telemetry) =>
        new(
            session.SessionId,
            session.ProcessId,
            session.ProcessStartTimeUtc,
            session.ProcessName,
            session.ExecutablePath,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.EndReason,
            telemetry,
            session.IncidentIds);

    private static WorkloadSession ReplaceIncidentIds(
        WorkloadSession session,
        IReadOnlyList<Guid> incidentIds) =>
        new(
            session.SessionId,
            session.ProcessId,
            session.ProcessStartTimeUtc,
            session.ProcessName,
            session.ExecutablePath,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.EndReason,
            session.Telemetry,
            incidentIds);

    private void ReplaceCachedSession(WorkloadSession updated)
    {
        var index = _sessions.FindIndex(x => x.SessionId == updated.SessionId);
        if (index >= 0)
        {
            _sessions[index] = updated;
        }
    }

    private static SessionTelemetrySummary Accumulate(
        SessionTelemetrySummary current,
        TelemetryFrame frame)
    {
        return new SessionTelemetrySummary(
            current.FrameCount + 1,
            Max(current.PeakCpuUtilizationPercent, Available(frame.Cpu.TotalUtilizationPercent)),
            Max(current.PeakGpuUtilizationPercent, MaxAvailable(frame.Gpus.Select(x => x.CoreUtilizationPercent))),
            Max(current.PeakGpuHotspotCelsius, MaxAvailable(frame.Gpus.Select(x => x.HotspotTemperatureCelsius))),
            Max(current.PeakGpuMemoryUsedMiB, MaxAvailable(frame.Gpus.Select(x => x.DedicatedMemoryUsedMiB))),
            Max(current.PeakSystemMemoryLoadPercent, Available(frame.System.PhysicalMemoryLoadPercent)));
    }

    private static double? Available(MetricReading reading) =>
        reading.IsAvailable ? reading.Value : null;

    private static double? MaxAvailable(IEnumerable<MetricReading> readings)
    {
        double? maximum = null;
        foreach (var reading in readings)
        {
            if (!reading.IsAvailable)
            {
                continue;
            }

            maximum = Max(maximum, reading.Value);
        }

        return maximum;
    }

    private static double? Max(double? left, double? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return Math.Max(left.Value, right.Value);
    }
}

internal sealed class SessionAssociatingIncidentReportSink : IIncidentReportSink
{
    private readonly IIncidentReportSink _inner;
    private readonly WorkloadSessionManager _sessions;

    public SessionAssociatingIncidentReportSink(
        PersistentIncidentReportSink inner,
        WorkloadSessionManager sessions)
    {
        _inner = inner;
        _sessions = sessions;
    }

    public async ValueTask WriteAsync(
        IncidentReport report,
        CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(report, cancellationToken).ConfigureAwait(false);
        await _sessions.AssociateIncidentAsync(report, cancellationToken).ConfigureAwait(false);
    }
}
