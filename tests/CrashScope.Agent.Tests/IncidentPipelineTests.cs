using System.Diagnostics;
using CrashScope.Agent.Buffering;
using CrashScope.Agent.Incidents;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class IncidentPipelineTests
{
    [Fact]
    public void Deduplicator_CollapsesRepeatedWerEventsAndRelatedDumpArtifact()
    {
        var deduplicator = new DiagnosticEvidenceDeduplicator();
        var observed = new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero);
        const string dumpPath = @"C:\Windows\LiveKernelReports\WATCHDOG\WATCHDOG-20260831-1649.dmp";
        const string reportId = "01234567-89ab-cdef-0123-456789abcdef";
        var message = $"""
            Problem signature:
            Event Name: LiveKernelEvent
            P1: 193
            P2: 80e
            {dumpPath}
            Report Id: {reportId}
            """;

        var first = CreateEvent(1001, 10, observed, message);
        var repeated = CreateEvent(1001, 11, observed.AddMinutes(1), message);
        var artifact = new DiagnosticArtifact(
            "WindowsErrorReporting",
            DiagnosticArtifactKind.WindowsErrorReport,
            @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive\x\Report.wer",
            observed.AddSeconds(2),
            null,
            reportId,
            "LiveKernelEvent",
            dumpPath,
            "different-signature");

        Assert.True(deduplicator.TryAccept(first));
        Assert.False(deduplicator.TryAccept(repeated));
        Assert.False(deduplicator.TryAccept(artifact));
    }

    [Fact]
    public void Deduplicator_DoesNotCollapseDifferentReportsWithSameSignature()
    {
        var deduplicator = new DiagnosticEvidenceDeduplicator();
        var observed = new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero);

        var first = new DiagnosticArtifact(
            "WindowsErrorReporting",
            DiagnosticArtifactKind.WindowsErrorReport,
            @"C:\WER\ReportArchive\incident-a\Report.wer",
            observed,
            null,
            "report-a",
            "LiveKernelEvent",
            @"C:\Windows\LiveKernelReports\WATCHDOG\a.dmp",
            "SAME-SIGNATURE");
        var second = new DiagnosticArtifact(
            "WindowsErrorReporting",
            DiagnosticArtifactKind.WindowsErrorReport,
            @"C:\WER\ReportArchive\incident-b\Report.wer",
            observed.AddMinutes(5),
            null,
            "report-b",
            "LiveKernelEvent",
            @"C:\Windows\LiveKernelReports\WATCHDOG\b.dmp",
            "SAME-SIGNATURE");

        Assert.True(deduplicator.TryAccept(first));
        Assert.True(deduplicator.TryAccept(second));
    }

    [Fact]
    public void Deduplicator_KeepsDifferentOrdinaryEventRecords()
    {
        var deduplicator = new DiagnosticEvidenceDeduplicator();
        var observed = new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero);

        var first = new DiagnosticEvent(
            "WindowsEventLog",
            "Application",
            "Application Error",
            1000,
            20,
            observed,
            null,
            DiagnosticEventKind.ApplicationFault,
            "Error",
            "first");
        var second = new DiagnosticEvent(
            first.Source,
            first.LogName,
            first.ProviderName,
            first.EventId,
            21,
            observed.AddSeconds(1),
            first.SourceOccurredAtUtc,
            first.Kind,
            first.Level,
            "second");

        Assert.True(deduplicator.TryAccept(first));
        Assert.True(deduplicator.TryAccept(second));
    }

    [Fact]
    public async Task IncidentCoordinator_CapturesSixtySecondsBeforeAndThirtyAfter()
    {
        var buffer = new TelemetryRingBuffer();
        var coordinator = new IncidentCoordinator(buffer);

        await coordinator.WriteAsync(CreateFrame(0, 39));
        await coordinator.WriteAsync(CreateFrame(1, 40));
        await coordinator.WriteAsync(CreateFrame(2, 90));
        await coordinator.WriteAsync(CreateFrame(3, 100));

        var trigger = CreateTrigger(100);
        var captureTask = coordinator.BeginCapture(trigger);

        await coordinator.WriteAsync(CreateFrame(4, 110));
        Assert.False(captureTask.IsCompleted);

        await coordinator.WriteAsync(CreateFrame(5, 129));
        Assert.False(captureTask.IsCompleted);

        await coordinator.WriteAsync(CreateFrame(6, 130));
        var capture = await captureTask;

        Assert.Equal(
            new long[] { 1, 2, 3, 4, 5, 6 },
            capture.TelemetryFrames.Select(x => x.Sequence));
        Assert.Equal(trigger, capture.Trigger);
    }

    [Fact]
    public async Task IncidentCoordinator_CompletesImmediatelyForRetroactiveWindow()
    {
        var buffer = new TelemetryRingBuffer();
        var coordinator = new IncidentCoordinator(buffer);

        await coordinator.WriteAsync(CreateFrame(0, 40));
        await coordinator.WriteAsync(CreateFrame(1, 100));
        await coordinator.WriteAsync(CreateFrame(2, 130));

        var captureTask = coordinator.BeginCapture(CreateTrigger(100));

        Assert.True(captureTask.IsCompletedSuccessfully);
        var capture = await captureTask;
        Assert.Equal(new long[] { 0, 1, 2 },
            capture.TelemetryFrames.Select(x => x.Sequence));
    }

    [Fact]
    public void IncidentTrigger_RejectsNonUtcObservationTime()
    {
        Assert.Throws<ArgumentException>(() => new IncidentTrigger(
            Guid.NewGuid(),
            "evidence",
            "summary",
            new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.FromHours(9)),
            null,
            TicksForSeconds(100)));
    }

    private static DiagnosticEvent CreateEvent(
        int eventId,
        long recordId,
        DateTimeOffset observedAtUtc,
        string message) =>
        new(
            "WindowsEventLog",
            "Application",
            "Windows Error Reporting",
            eventId,
            recordId,
            observedAtUtc,
            null,
            DiagnosticEventKind.WindowsErrorReport,
            "Information",
            message);

    private static IncidentTrigger CreateTrigger(double seconds) =>
        new(
            Guid.NewGuid(),
            "wer:watchdog",
            "Windows watchdog evidence observed",
            Epoch.AddSeconds(seconds),
            null,
            TicksForSeconds(seconds));

    private static TelemetryFrame CreateFrame(long sequence, double seconds)
    {
        var unavailable = MetricReading.Unsupported();
        var system = new SystemTelemetry(unavailable, unavailable, unavailable);
        var cpu = new CpuTelemetry(
            unavailable,
            Array.Empty<LogicalProcessorLoad>(),
            unavailable,
            unavailable,
            unavailable);

        return new TelemetryFrame(
            sequence,
            Epoch.AddSeconds(seconds),
            TicksForSeconds(seconds),
            system,
            cpu,
            Array.Empty<GpuTelemetry>());
    }

    private static long TicksForSeconds(double seconds) =>
        (long)(seconds * Stopwatch.Frequency);

    private static DateTimeOffset Epoch { get; } =
        new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
}
