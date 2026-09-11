using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Evidence;

namespace CrashScope.Agent.Incidents;

internal sealed class LiveIncidentMonitor
{
    public static TimeSpan DefaultScanInterval { get; } = TimeSpan.FromSeconds(2);
    public static TimeSpan DefaultReconciliationInterval { get; } = TimeSpan.FromSeconds(60);
    public static TimeSpan DefaultOverlap { get; } = TimeSpan.FromSeconds(5);
    public static TimeSpan DefaultFreshnessWindow { get; } = TimeSpan.FromMinutes(2);

    private readonly IDiagnosticEventSource _eventSource;
    private readonly IDiagnosticArtifactSource _artifactSource;
    private readonly DiagnosticEvidenceDeduplicator _deduplicator;
    private readonly IncidentCoordinator _incidentCoordinator;
    private readonly IncidentReportBuilder _reportBuilder;
    private readonly IIncidentReportSink _reportSink;
    private readonly ISamplingClock _clock;
    private readonly TimeSpan _scanInterval;
    private readonly TimeSpan _reconciliationInterval;
    private readonly TimeSpan _overlap;
    private readonly TimeSpan _freshnessWindow;
    private readonly Func<CancellationToken, ValueTask<ProcessObservation?>>? _processObservationProvider;
    private readonly Func<
        DateTimeOffset,
        DateTimeOffset,
        CancellationToken,
        ValueTask<IReadOnlyList<EvidenceEvent>>>? _providerEvidenceSource;
    private readonly object _pendingSync = new();
    private readonly HashSet<Task> _pendingReports = new();
    private DateTimeOffset _cursorUtc;

    public LiveIncidentMonitor(
        IDiagnosticEventSource eventSource,
        IDiagnosticArtifactSource artifactSource,
        DiagnosticEvidenceDeduplicator deduplicator,
        IncidentCoordinator incidentCoordinator,
        IncidentReportBuilder reportBuilder,
        IIncidentReportSink reportSink,
        ISamplingClock clock,
        TimeSpan? scanInterval = null,
        TimeSpan? overlap = null,
        TimeSpan? freshnessWindow = null,
        Func<CancellationToken, ValueTask<ProcessObservation?>>? processObservationProvider = null,
        TimeSpan? reconciliationInterval = null,
        Func<
            DateTimeOffset,
            DateTimeOffset,
            CancellationToken,
            ValueTask<IReadOnlyList<EvidenceEvent>>>? providerEvidenceSource = null)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        ArgumentNullException.ThrowIfNull(artifactSource);
        ArgumentNullException.ThrowIfNull(deduplicator);
        ArgumentNullException.ThrowIfNull(incidentCoordinator);
        ArgumentNullException.ThrowIfNull(reportBuilder);
        ArgumentNullException.ThrowIfNull(reportSink);
        ArgumentNullException.ThrowIfNull(clock);

        _eventSource = eventSource;
        _artifactSource = artifactSource;
        _deduplicator = deduplicator;
        _incidentCoordinator = incidentCoordinator;
        _reportBuilder = reportBuilder;
        _reportSink = reportSink;
        _clock = clock;
        _scanInterval = scanInterval ?? DefaultScanInterval;
        _reconciliationInterval = reconciliationInterval ?? DefaultReconciliationInterval;
        _overlap = overlap ?? DefaultOverlap;
        _freshnessWindow = freshnessWindow ?? DefaultFreshnessWindow;
        _processObservationProvider = processObservationProvider;
        _providerEvidenceSource = providerEvidenceSource;

        if (_scanInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(scanInterval));
        }

        if (_reconciliationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciliationInterval));
        }

        if (_overlap < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap));
        }

        if (_freshnessWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(freshnessWindow));
        }

        _cursorUtc = _clock.GetUtcNow().ToUniversalTime();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Seed event history and artifact state once before switching to realtime waits.
        await ScanOnceAsync(cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_eventSource is IRealtimeDiagnosticEventSource realtime &&
                realtime.RealtimeAvailable)
            {
                var reconcile = await WaitForRealtimeOrReconciliationAsync(
                        realtime,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (reconcile)
                {
                    await realtime.ReconcileAsync(
                            _cursorUtc,
                            2048,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await ScanOnceAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Safe compatibility path if Windows subscriptions are unavailable.
            await _clock.DelayAsync(_scanInterval, cancellationToken).ConfigureAwait(false);
            await ScanOnceAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> WaitForRealtimeOrReconciliationAsync(
        IRealtimeDiagnosticEventSource realtime,
        CancellationToken cancellationToken)
    {
        using var cycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var eventTask = realtime.WaitForEventAsync(cycleCancellation.Token).AsTask();
        var reconciliationTask = _clock
            .DelayAsync(_reconciliationInterval, cycleCancellation.Token)
            .AsTask();

        var completed = await Task.WhenAny(eventTask, reconciliationTask)
            .ConfigureAwait(false);
        var reconciliationDue = completed == reconciliationTask;

        await cycleCancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        return reconciliationDue;
    }

    internal async ValueTask<int> ScanOnceAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scanStartedUtc = _clock.GetUtcNow().ToUniversalTime();
        var sinceUtc = _cursorUtc;

        var events = await _eventSource
            .ReadSinceAsync(sinceUtc, 512, cancellationToken)
            .ConfigureAwait(false);
        var artifacts = await _artifactSource
            .ReadSinceAsync(sinceUtc, 512, cancellationToken)
            .ConfigureAwait(false);

        _cursorUtc = scanStartedUtc - _overlap;

        var triggerCount = 0;

        foreach (var artifact in artifacts.OrderBy(ArtifactOccurrenceTimeUtc))
        {
            if (!IsTriggerWorthy(artifact) ||
                !IsFresh(ArtifactOccurrenceTimeUtc(artifact), scanStartedUtc) ||
                !_deduplicator.TryAccept(artifact))
            {
                continue;
            }

            await StartCaptureAsync(
                CreateTrigger(artifact),
                cancellationToken).ConfigureAwait(false);
            triggerCount++;
        }

        foreach (var diagnosticEvent in events.OrderBy(EventOccurrenceTimeUtc))
        {
            if (!IsTriggerWorthy(diagnosticEvent) ||
                !IsFresh(EventOccurrenceTimeUtc(diagnosticEvent), scanStartedUtc) ||
                !_deduplicator.TryAccept(diagnosticEvent))
            {
                continue;
            }

            await StartCaptureAsync(
                CreateTrigger(diagnosticEvent),
                cancellationToken).ConfigureAwait(false);
            triggerCount++;
        }

        return triggerCount;
    }

    internal IReadOnlyCollection<Task> PendingReportsSnapshot()
    {
        lock (_pendingSync)
        {
            return _pendingReports.ToArray();
        }
    }

    private async ValueTask StartCaptureAsync(
        IncidentTrigger trigger,
        CancellationToken cancellationToken)
    {
        ProcessObservation? processObservation = null;
        if (_processObservationProvider is not null)
        {
            processObservation = await _processObservationProvider(cancellationToken)
                .ConfigureAwait(false);
        }

        var captureTask = _incidentCoordinator.BeginCapture(trigger);
        var reportTask = CompleteReportAsync(captureTask, processObservation);

        lock (_pendingSync)
        {
            _pendingReports.Add(reportTask);
        }

        _ = reportTask.ContinueWith(
            completed =>
            {
                lock (_pendingSync)
                {
                    _pendingReports.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task CompleteReportAsync(
        Task<IncidentCapture> captureTask,
        ProcessObservation? processObservation)
    {
        var capture = await captureTask.ConfigureAwait(false);
        var incidentTimeUtc = capture.Trigger.SourceOccurredAtUtc
            ?? capture.Trigger.ObservedAtUtc;
        var sinceUtc = incidentTimeUtc - TimeSpan.FromMinutes(2);

        var events = await _eventSource
            .ReadSinceAsync(sinceUtc, 512, CancellationToken.None)
            .ConfigureAwait(false);
        var artifacts = await _artifactSource
            .ReadSinceAsync(sinceUtc, 512, CancellationToken.None)
            .ConfigureAwait(false);

        IReadOnlyList<EvidenceEvent> providerEvidence = Array.Empty<EvidenceEvent>();
        if (_providerEvidenceSource is not null)
        {
            try
            {
                providerEvidence = await _providerEvidenceSource(
                        incidentTimeUtc - _reportBuilder.EvidenceWindow,
                        incidentTimeUtc + _reportBuilder.EvidenceWindow,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Optional evidence providers must never block the core incident report.
            }
        }

        var report = _reportBuilder.Build(
            capture,
            events,
            artifacts,
            processObservation,
            providerEvidence);

        await _reportSink.WriteAsync(report, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private bool IsFresh(DateTimeOffset occurredAtUtc, DateTimeOffset scanStartedUtc)
    {
        if (occurredAtUtc > scanStartedUtc + _overlap)
        {
            return false;
        }

        return scanStartedUtc - occurredAtUtc <= _freshnessWindow;
    }

    private static bool IsTriggerWorthy(DiagnosticArtifact artifact)
    {
        var eventType = artifact.EventType;
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return artifact.Kind == DiagnosticArtifactKind.LiveKernelDump;
        }

        return eventType.Equals("LiveKernelEvent", StringComparison.OrdinalIgnoreCase) ||
            eventType.Equals("BlueScreen", StringComparison.OrdinalIgnoreCase) ||
            eventType.StartsWith("APPCRASH", StringComparison.OrdinalIgnoreCase) ||
            eventType.StartsWith("AppHang", StringComparison.OrdinalIgnoreCase) ||
            eventType.StartsWith("BEX", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTriggerWorthy(DiagnosticEvent diagnosticEvent) =>
        diagnosticEvent.Kind switch
        {
            DiagnosticEventKind.ApplicationFault => true,
            DiagnosticEventKind.ApplicationHang => true,
            DiagnosticEventKind.HardwareError => true,
            DiagnosticEventKind.DisplayDriver => true,
            DiagnosticEventKind.KernelPower => diagnosticEvent.EventId == 41,
            _ => false
        };

    private IncidentTrigger CreateTrigger(DiagnosticArtifact artifact)
    {
        var sourceTimeUtc = ArtifactOccurrenceTimeUtc(artifact);
        var key = !string.IsNullOrWhiteSpace(artifact.ReportId)
            ? $"report:{artifact.ReportId}"
            : !string.IsNullOrWhiteSpace(artifact.RelatedPath)
                ? $"path:{artifact.RelatedPath}"
                : $"artifact:{artifact.ArtifactPath}";

        var summary = !string.IsNullOrWhiteSpace(artifact.EventType)
            ? $"Windows reported {artifact.EventType} evidence."
            : "Windows diagnostic artifact evidence was observed.";

        return new IncidentTrigger(
            Guid.NewGuid(),
            key,
            summary,
            artifact.ObservedAtUtc,
            sourceTimeUtc,
            _clock.GetTimestamp());
    }

    private IncidentTrigger CreateTrigger(DiagnosticEvent diagnosticEvent)
    {
        var key = diagnosticEvent.RecordId.HasValue
            ? $"event:{diagnosticEvent.LogName}:{diagnosticEvent.ProviderName}:{diagnosticEvent.RecordId.Value}"
            : $"event:{diagnosticEvent.ProviderName}:{diagnosticEvent.EventId}:{diagnosticEvent.ObservedAtUtc.UtcTicks}";

        return new IncidentTrigger(
            Guid.NewGuid(),
            key,
            $"{diagnosticEvent.ProviderName} event {diagnosticEvent.EventId} was observed.",
            diagnosticEvent.ObservedAtUtc,
            EventOccurrenceTimeUtc(diagnosticEvent),
            _clock.GetTimestamp());
    }

    private static DateTimeOffset EventOccurrenceTimeUtc(DiagnosticEvent diagnosticEvent) =>
        diagnosticEvent.SourceOccurredAtUtc ?? diagnosticEvent.ObservedAtUtc;

    private static DateTimeOffset ArtifactOccurrenceTimeUtc(DiagnosticArtifact artifact)
    {
        if (artifact.SourceOccurredAtUtc.HasValue)
        {
            return artifact.SourceOccurredAtUtc.Value;
        }

        return IncidentReportBuilder.TryGetWatchdogSourceTimeUtc(
            artifact.RelatedPath,
            out var sourceTimeUtc)
                ? sourceTimeUtc
                : artifact.ObservedAtUtc;
    }
}
