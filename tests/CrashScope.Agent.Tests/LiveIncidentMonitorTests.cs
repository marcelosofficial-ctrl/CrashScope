using System.Diagnostics;
using CrashScope.Agent.Buffering;
using CrashScope.Agent.Incidents;
using CrashScope.Agent.Sampling;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Incidents;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class LiveIncidentMonitorTests
{
    [Fact]
    public async Task ScanOnceAsync_RecentLiveKernelArtifactProducesReport()
    {
        var now = Utc(2026, 9, 8, 7, 0, 0);
        var artifact = CreateArtifact("LiveKernelEvent", now, "report-1");
        var sink = new InMemoryIncidentReportSink();
        var monitor = CreateMonitor(now, sink, artifacts: new[] { artifact });

        var triggered = await monitor.ScanOnceAsync();

        Assert.Equal(1, triggered);
        var report = Assert.Single(sink.Snapshot());
        Assert.Equal(IncidentClassification.KernelOrDriverWatchdog, report.Classification);
    }

    [Fact]
    public async Task ScanOnceAsync_DoesNotReplayStaleHistoricalArtifact()
    {
        var now = Utc(2026, 9, 8, 7, 0, 0);
        var artifact = CreateArtifact(
            "LiveKernelEvent",
            now.AddHours(-6),
            "old-report");
        var sink = new InMemoryIncidentReportSink();
        var monitor = CreateMonitor(now, sink, artifacts: new[] { artifact });

        var triggered = await monitor.ScanOnceAsync();

        Assert.Equal(0, triggered);
        Assert.Empty(sink.Snapshot());
    }

    [Fact]
    public async Task ScanOnceAsync_IgnoresRadarPreLeakWerNoise()
    {
        var now = Utc(2026, 9, 8, 7, 0, 0);
        var artifact = CreateArtifact(
            "RADAR_PRE_LEAK_64",
            now,
            "radar-report");
        var sink = new InMemoryIncidentReportSink();
        var monitor = CreateMonitor(now, sink, artifacts: new[] { artifact });

        var triggered = await monitor.ScanOnceAsync();

        Assert.Equal(0, triggered);
        Assert.Empty(sink.Snapshot());
    }

    [Fact]
    public async Task ScanOnceAsync_OnlyKernelPower41Triggers()
    {
        var now = Utc(2026, 9, 8, 7, 0, 0);
        var ignored = CreateEvent(
            DiagnosticEventKind.KernelPower,
            "Microsoft-Windows-Kernel-Power",
            577,
            1,
            now);
        var trigger = CreateEvent(
            DiagnosticEventKind.KernelPower,
            "Microsoft-Windows-Kernel-Power",
            41,
            2,
            now);
        var sink = new InMemoryIncidentReportSink();
        var monitor = CreateMonitor(now, sink, events: new[] { ignored, trigger });

        var triggered = await monitor.ScanOnceAsync();

        Assert.Equal(1, triggered);
        var report = Assert.Single(sink.Snapshot());
        Assert.Equal(IncidentClassification.UnexpectedShutdown, report.Classification);
    }

    [Fact]
    public async Task ScanOnceAsync_DeduplicatesRepeatedEvidenceAcrossScans()
    {
        var now = Utc(2026, 9, 8, 7, 0, 0);
        var artifact = CreateArtifact("LiveKernelEvent", now, "same-report");
        var sink = new InMemoryIncidentReportSink();
        var monitor = CreateMonitor(now, sink, artifacts: new[] { artifact });

        var first = await monitor.ScanOnceAsync();
        var second = await monitor.ScanOnceAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(sink.Snapshot());
    }

    private static LiveIncidentMonitor CreateMonitor(
        DateTimeOffset now,
        InMemoryIncidentReportSink sink,
        IReadOnlyList<DiagnosticEvent>? events = null,
        IReadOnlyList<DiagnosticArtifact>? artifacts = null)
    {
        var clock = new FakeSamplingClock(now, 10 * Stopwatch.Frequency);
        var buffer = new TelemetryRingBuffer();
        var coordinator = new IncidentCoordinator(
            buffer,
            postTriggerWindow: TimeSpan.Zero);

        coordinator.WriteAsync(CreateFrame(now, 10 * Stopwatch.Frequency))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return new LiveIncidentMonitor(
            new FakeEventSource(events ?? Array.Empty<DiagnosticEvent>()),
            new FakeArtifactSource(artifacts ?? Array.Empty<DiagnosticArtifact>()),
            new DiagnosticEvidenceDeduplicator(),
            coordinator,
            new IncidentReportBuilder(),
            sink,
            clock,
            freshnessWindow: TimeSpan.FromMinutes(2));
    }

    private static DiagnosticArtifact CreateArtifact(
        string eventType,
        DateTimeOffset occurredAtUtc,
        string reportId) =>
        new(
            "WindowsErrorReportingEventLog",
            DiagnosticArtifactKind.WindowsErrorReport,
            $"eventlog://Application/Windows Error Reporting/{reportId}",
            occurredAtUtc,
            occurredAtUtc,
            reportId,
            eventType,
            eventType == "LiveKernelEvent"
                ? @"C:\Windows\LiveKernelReports\WATCHDOG\sample.dmp"
                : null,
            "signature");

    private static DiagnosticEvent CreateEvent(
        DiagnosticEventKind kind,
        string provider,
        int eventId,
        long recordId,
        DateTimeOffset occurredAtUtc) =>
        new(
            "WindowsEventLog",
            "System",
            provider,
            eventId,
            recordId,
            occurredAtUtc,
            occurredAtUtc,
            kind,
            "Error",
            "test");

    private static TelemetryFrame CreateFrame(
        DateTimeOffset timestampUtc,
        long monotonicTimestamp)
    {
        var available = MetricReading.Available(10);
        var unsupported = MetricReading.Unsupported();
        var system = new SystemTelemetry(available, available, available);
        var cpu = new CpuTelemetry(
            available,
            Array.Empty<LogicalProcessorLoad>(),
            unsupported,
            unsupported,
            unsupported);

        return new TelemetryFrame(
            0,
            timestampUtc,
            monotonicTimestamp,
            system,
            cpu,
            Array.Empty<GpuTelemetry>());
    }

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private sealed class FakeEventSource : IDiagnosticEventSource
    {
        private readonly IReadOnlyList<DiagnosticEvent> _events;

        public FakeEventSource(IReadOnlyList<DiagnosticEvent> events)
        {
            _events = events;
        }

        public string Name => "FakeEvents";

        public ValueTask<IReadOnlyList<DiagnosticEvent>> ReadSinceAsync(
            DateTimeOffset sinceUtc,
            int maximumEvents = 256,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DiagnosticEvent>>(
                _events.Where(x => x.ObservedAtUtc >= sinceUtc)
                    .Take(maximumEvents)
                    .ToArray());
    }

    private sealed class FakeArtifactSource : IDiagnosticArtifactSource
    {
        private readonly IReadOnlyList<DiagnosticArtifact> _artifacts;

        public FakeArtifactSource(IReadOnlyList<DiagnosticArtifact> artifacts)
        {
            _artifacts = artifacts;
        }

        public string Name => "FakeArtifacts";

        public ValueTask<IReadOnlyList<DiagnosticArtifact>> ReadSinceAsync(
            DateTimeOffset sinceUtc,
            int maximumArtifacts = 256,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DiagnosticArtifact>>(
                _artifacts.Where(x => x.ObservedAtUtc >= sinceUtc)
                    .Take(maximumArtifacts)
                    .ToArray());
    }

    private sealed class FakeSamplingClock : ISamplingClock
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public FakeSamplingClock(DateTimeOffset utcNow, long timestamp)
        {
            _utcNow = utcNow;
            _timestamp = timestamp;
        }

        public DateTimeOffset GetUtcNow() => _utcNow;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp) =>
            Stopwatch.GetElapsedTime(startTimestamp, endTimestamp);

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _utcNow += delay;
            _timestamp += (long)(delay.TotalSeconds * Stopwatch.Frequency);
            return ValueTask.CompletedTask;
        }
    }
}
