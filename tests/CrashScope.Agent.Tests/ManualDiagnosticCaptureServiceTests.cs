using CrashScope.Agent.Buffering;
using CrashScope.Agent.Incidents;
using CrashScope.Agent.Processes;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Core.Incidents;
using CrashScope.Core.Sessions;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class ManualDiagnosticCaptureServiceTests
{
    [Fact]
    public async Task CaptureCreatesExplicitUserMarkerAndUsesExistingTelemetry()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now, 100);
        var buffer = new TelemetryRingBuffer();
        var coordinator = new IncidentCoordinator(
            buffer,
            preTriggerWindow: TimeSpan.FromSeconds(60),
            postTriggerWindow: TimeSpan.Zero);
        await coordinator.WriteAsync(Frame(1, now.AddSeconds(-1), 90));

        var sink = new CollectingSink();
        var sessions = new WorkloadSessionManager(
            new MonitoredProcessTracker(new NeverRunningProbe()),
            new SamplingModeController(),
            new MemorySessionRepository(),
            clock);
        var service = new ManualDiagnosticCaptureService(coordinator, sink, sessions, clock);

        var report = await service.CaptureAsync();

        Assert.Equal(IncidentClassification.UserDiagnosticMarker, report.Classification);
        Assert.Contains("not evidence", report.Assessment, StringComparison.OrdinalIgnoreCase);
        Assert.Single(report.Evidence);
        Assert.Equal(IncidentEvidenceRole.Trigger, report.Evidence[0].Role);
        Assert.Equal("DiagnosticMarker", report.Evidence[0].Kind);
        Assert.Equal(1, report.Telemetry.FrameCount);
        Assert.Same(report, sink.Report);
        Assert.False(service.IsCaptureInProgress);
    }

    private static TelemetryFrame Frame(long sequence, DateTimeOffset timestampUtc, long monotonic)
    {
        var unavailable = MetricReading.Unsupported();
        return new TelemetryFrame(
            sequence,
            timestampUtc,
            monotonic,
            new SystemTelemetry(unavailable, unavailable, unavailable),
            new CpuTelemetry(
                unavailable,
                Array.Empty<LogicalProcessorLoad>(),
                unavailable,
                unavailable,
                unavailable),
            Array.Empty<GpuTelemetry>());
    }

    private sealed class CollectingSink : IIncidentReportSink
    {
        public IncidentReport? Report { get; private set; }

        public ValueTask WriteAsync(
            IncidentReport report,
            CancellationToken cancellationToken = default)
        {
            Report = report;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NeverRunningProbe : IProcessProbe
    {
        public ValueTask<ProcessProbeResult> ProbeAsync(
            int processId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProcessProbeResult(
                ProcessProbeState.NotFound,
                processId,
                null,
                null,
                null,
                "not running"));
    }

    private sealed class MemorySessionRepository : IWorkloadSessionRepository
    {
        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(
            WorkloadSession session,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<WorkloadSession>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkloadSession>>(Array.Empty<WorkloadSession>());
    }

    private sealed class FakeClock : ISamplingClock
    {
        private readonly DateTimeOffset _utcNow;
        private readonly long _timestamp;

        public FakeClock(DateTimeOffset utcNow, long timestamp)
        {
            _utcNow = utcNow;
            _timestamp = timestamp;
        }

        public DateTimeOffset GetUtcNow() => _utcNow;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) => TimeSpan.Zero;
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
