using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Incidents;
using CrashScope.Core.Sessions;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class WorkloadSessionManagerTests
{
    [Fact]
    public async Task AttachStartsActiveSamplingAndStopReturnsToBackground()
    {
        var start = Utc(8, 0);
        var repository = new FakeRepository();
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(Running(4242, start, "game")),
            repository,
            mode,
            new FakeClock(Utc(8, 5)));
        await manager.InitializeAsync();

        var attached = await manager.AttachAsync(4242);

        Assert.True(attached.IsAttached);
        Assert.Equal(SamplingMode.Active, mode.Current);
        Assert.NotNull(manager.ActiveSnapshot());

        var stopped = await manager.StopAsync();

        Assert.NotNull(stopped);
        Assert.Equal(SessionEndReason.UserStopped, stopped!.EndReason);
        Assert.Equal(SamplingMode.Background, mode.Current);
        Assert.Null(manager.ActiveSnapshot());
    }

    [Fact]
    public async Task ProcessExitEndsSessionWithoutCallingItCrash()
    {
        var start = Utc(8, 0);
        var mode = new SamplingModeController();
        var manager = Manager(
            new FakeProbe(
                Running(4242, start, "game"),
                new ProcessProbeResult(
                    ProcessProbeState.NotFound,
                    4242,
                    null,
                    null,
                    null,
                    "Process was not found.")),
            new FakeRepository(),
            mode,
            new FakeClock(Utc(8, 10)));
        await manager.InitializeAsync();
        await manager.AttachAsync(4242);

        var observation = await manager.ObserveActiveProcessAsync();

        Assert.Equal(ProcessObservationState.Exited, observation!.State);
        var session = Assert.Single(manager.Snapshot());
        Assert.Equal(SessionEndReason.ProcessExited, session.EndReason);
        Assert.DoesNotContain("crash", session.EndReason.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SamplingMode.Background, mode.Current);
    }

    [Fact]
    public async Task PidReuseEndsOriginalSessionSafely()
    {
        var start = Utc(8, 0);
        var replacement = Utc(8, 30);
        var manager = Manager(
            new FakeProbe(
                Running(4242, start, "game"),
                Running(4242, replacement, "different")),
            new FakeRepository(),
            new SamplingModeController(),
            new FakeClock(Utc(8, 40)));
        await manager.InitializeAsync();
        await manager.AttachAsync(4242);

        var observation = await manager.ObserveActiveProcessAsync();

        Assert.Equal(ProcessObservationState.PidReused, observation!.State);
        Assert.Equal(SessionEndReason.PidReused, Assert.Single(manager.Snapshot()).EndReason);
        Assert.Null(manager.ActiveSnapshot());
    }

    [Fact]
    public async Task RestartReconcilesPersistedActiveSessionAsInterrupted()
    {
        var active = new WorkloadSession(
            Guid.NewGuid(),
            4242,
            Utc(7, 50),
            "game",
            null,
            Utc(8, 0),
            null,
            null,
            SessionTelemetrySummary.Empty);
        var repository = new FakeRepository(active);
        var manager = Manager(
            new FakeProbe(),
            repository,
            new SamplingModeController(),
            new FakeClock(Utc(9, 0)));

        await manager.InitializeAsync();

        var session = Assert.Single(manager.Snapshot());
        Assert.False(session.IsActive);
        Assert.Equal(SessionEndReason.InterruptedOnRestart, session.EndReason);
        Assert.Equal(Utc(9, 0), session.EndedAtUtc);
        Assert.Null(manager.ActiveSnapshot());
    }

    [Fact]
    public async Task TelemetryFramesAccumulatePeaksWithoutPersistingEveryFrame()
    {
        var repository = new FakeRepository();
        var manager = Manager(
            new FakeProbe(Running(4242, Utc(8, 0), "game")),
            repository,
            new SamplingModeController(),
            new FakeClock(Utc(8, 5)));
        await manager.InitializeAsync();
        await manager.AttachAsync(4242);
        var savesAfterAttach = repository.SaveCount;

        await manager.WriteAsync(Frame(1, 50, 70, 75, 8000, 40));
        await manager.WriteAsync(Frame(2, 80, 99, 90, 15000, 72));

        var session = manager.ActiveSnapshot()!;
        Assert.Equal(2, session.Telemetry.FrameCount);
        Assert.Equal(80, session.Telemetry.PeakCpuUtilizationPercent);
        Assert.Equal(99, session.Telemetry.PeakGpuUtilizationPercent);
        Assert.Equal(90, session.Telemetry.PeakGpuHotspotCelsius);
        Assert.Equal(15000, session.Telemetry.PeakGpuMemoryUsedMiB);
        Assert.Equal(72, session.Telemetry.PeakSystemMemoryLoadPercent);
        Assert.Equal(savesAfterAttach, repository.SaveCount);
    }

    [Fact]
    public async Task MatchingIncidentIsAssociatedByProcessIdentity()
    {
        var processStart = Utc(8, 0);
        var repository = new FakeRepository();
        var clock = new FakeClock(Utc(8, 5));
        var manager = Manager(
            new FakeProbe(Running(4242, processStart, "game")),
            repository,
            new SamplingModeController(),
            clock);
        await manager.InitializeAsync();
        await manager.AttachAsync(4242);
        var report = Report(processStart, Utc(8, 6));

        var associated = await manager.AssociateIncidentAsync(report);

        Assert.True(associated);
        var session = manager.ActiveSnapshot()!;
        Assert.Contains(report.IncidentId, session.IncidentIds);
        Assert.Contains(report.IncidentId, repository.Saved[^1].IncidentIds);
    }

    private static WorkloadSessionManager Manager(
        IProcessProbe probe,
        IWorkloadSessionRepository repository,
        SamplingModeController mode,
        ISamplingClock clock) =>
        new(
            new MonitoredProcessTracker(probe),
            mode,
            repository,
            clock);

    private static ProcessProbeResult Running(
        int pid,
        DateTimeOffset start,
        string name) =>
        new(ProcessProbeState.Running, pid, start, name, $@"C:\Games\{name}.exe", null);

    private static TelemetryFrame Frame(
        long sequence,
        double cpu,
        double gpu,
        double hotspot,
        double vram,
        double memoryLoad)
    {
        var unavailable = MetricReading.Unsupported();
        var system = new SystemTelemetry(
            unavailable,
            unavailable,
            MetricReading.Available(memoryLoad));
        var cpuTelemetry = new CpuTelemetry(
            MetricReading.Available(cpu),
            Array.Empty<LogicalProcessorLoad>(),
            unavailable,
            unavailable,
            unavailable);
        var gpuTelemetry = new GpuTelemetry(
            "/gpu/0",
            "GPU",
            MetricReading.Available(gpu),
            unavailable,
            unavailable,
            MetricReading.Available(hotspot),
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            MetricReading.Available(vram),
            MetricReading.Available(16000),
            unavailable,
            unavailable);

        return new TelemetryFrame(
            sequence,
            Utc(8, 5).AddSeconds(sequence),
            sequence,
            system,
            cpuTelemetry,
            new[] { gpuTelemetry });
    }

    private static IncidentReport Report(
        DateTimeOffset processStart,
        DateTimeOffset incidentTime) =>
        new(
            Guid.NewGuid(),
            IncidentClassification.KernelOrDriverWatchdog,
            "Watchdog evidence",
            "Correlated evidence.",
            "Evidence does not by itself prove root cause.",
            incidentTime,
            new IncidentProcessContext(
                4242,
                processStart,
                "game",
                "Running",
                incidentTime),
            new IncidentTelemetrySummary(1, incidentTime, incidentTime, null, null, null, null, null),
            Array.Empty<IncidentEvidenceItem>());

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 8, hour, minute, 0, TimeSpan.Zero);

    private sealed class FakeProbe : IProcessProbe
    {
        private readonly Queue<ProcessProbeResult> _results;

        public FakeProbe(params ProcessProbeResult[] results)
        {
            _results = new Queue<ProcessProbeResult>(results);
        }

        public ValueTask<ProcessProbeResult> ProbeAsync(
            int processId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No fake process result remains.");
            }

            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeRepository : IWorkloadSessionRepository
    {
        private readonly List<WorkloadSession> _stored;

        public FakeRepository(params WorkloadSession[] sessions)
        {
            _stored = sessions.ToList();
        }

        public int SaveCount { get; private set; }
        public List<WorkloadSession> Saved { get; } = new();

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(
            WorkloadSession session,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Saved.Add(session);
            var index = _stored.FindIndex(x => x.SessionId == session.SessionId);
            if (index >= 0)
            {
                _stored[index] = session;
            }
            else
            {
                _stored.Add(session);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<WorkloadSession>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkloadSession>>(_stored.ToArray());
    }

    private sealed class FakeClock : ISamplingClock
    {
        private readonly DateTimeOffset _utcNow;

        public FakeClock(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public DateTimeOffset GetUtcNow() => _utcNow;
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
