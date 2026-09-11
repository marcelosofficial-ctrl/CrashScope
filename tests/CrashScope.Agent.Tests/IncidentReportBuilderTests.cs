using System.Diagnostics;
using CrashScope.Agent.Incidents;
using CrashScope.Agent.Processes;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Incidents;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Tests;

public sealed class IncidentReportBuilderTests
{
    [Fact]
    public void Build_ClassifiesProcessExitWithLiveKernelEvidenceAsWatchdogNotMixed()
    {
        var incidentTime = Utc(2026, 9, 6, 14, 20, 0);
        var capture = CreateCapture(incidentTime, CreateFrame(0, incidentTime, 75, 99, 88, 8000, 70));
        var process = CreateProcessObservation(ProcessObservationState.Exited, incidentTime.AddSeconds(1));
        var artifact = new DiagnosticArtifact(
            "WindowsErrorReportingEventLog",
            DiagnosticArtifactKind.WindowsErrorReport,
            "eventlog://Application/Windows Error Reporting/82464",
            incidentTime.AddSeconds(49),
            incidentTime,
            "8653662e-ffd6-4b35-8fd4-62a4c389982f",
            "LiveKernelEvent",
            @"C:\Windows\LiveKernelReports\WATCHDOG\WATCHDOG-20260906-2320.dmp",
            "sig");

        var report = new IncidentReportBuilder().Build(
            capture,
            Array.Empty<DiagnosticEvent>(),
            new[] { artifact },
            process);

        Assert.Equal(IncidentClassification.KernelOrDriverWatchdog, report.Classification);
        Assert.Contains("GPU/driver instability", report.Assessment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not treat it as proof", report.Assessment, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Exited", report.Process!.ObservationState);
    }

    [Fact]
    public void Build_UsesApplicationFailureWhenExitHasNoStrongerEvidence()
    {
        var incidentTime = Utc(2026, 9, 8, 4, 0, 0);
        var capture = CreateCapture(incidentTime);
        var process = CreateProcessObservation(ProcessObservationState.Exited, incidentTime);

        var report = new IncidentReportBuilder().Build(
            capture,
            Array.Empty<DiagnosticEvent>(),
            Array.Empty<DiagnosticArtifact>(),
            process);

        Assert.Equal(IncidentClassification.ApplicationFailure, report.Classification);
        Assert.Contains("does not infer a hardware cause", report.Assessment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ReportsMixedWhenIndependentEvidenceCategoriesOverlap()
    {
        var incidentTime = Utc(2026, 9, 8, 4, 0, 0);
        var capture = CreateCapture(incidentTime);
        var events = new[]
        {
            CreateEvent(DiagnosticEventKind.HardwareError, "Microsoft-Windows-WHEA-Logger", 18, incidentTime),
            CreateEvent(DiagnosticEventKind.DisplayDriver, "Display", 4101, incidentTime.AddSeconds(1))
        };

        var report = new IncidentReportBuilder().Build(
            capture,
            events,
            Array.Empty<DiagnosticArtifact>());

        Assert.Equal(IncidentClassification.Mixed, report.Classification);
        Assert.Contains("causality", report.Assessment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_SummarizesAvailableTelemetryPeaks()
    {
        var incidentTime = Utc(2026, 9, 8, 4, 0, 0);
        var frames = new[]
        {
            CreateFrame(0, incidentTime.AddSeconds(-1), 25, 40, 60, 4000, 55),
            CreateFrame(1, incidentTime, 90, 99, 95, 12000, 82),
            CreateFrame(2, incidentTime.AddSeconds(1), 50, 70, 80, 9000, 65)
        };

        var report = new IncidentReportBuilder().Build(
            CreateCapture(incidentTime, frames),
            Array.Empty<DiagnosticEvent>(),
            Array.Empty<DiagnosticArtifact>());

        Assert.Equal(3, report.Telemetry.FrameCount);
        Assert.Equal(90, report.Telemetry.PeakCpuUtilizationPercent);
        Assert.Equal(99, report.Telemetry.PeakGpuUtilizationPercent);
        Assert.Equal(95, report.Telemetry.PeakGpuHotspotCelsius);
        Assert.Equal(12000, report.Telemetry.PeakGpuMemoryUsedMiB);
        Assert.Equal(82, report.Telemetry.PeakSystemMemoryLoadPercent);
    }

    [Fact]
    public void Build_UsesWatchdogFilenameTimeToRejectStaleQueuedReport()
    {
        var incidentTime = Utc(2026, 9, 3, 12, 48, 47);
        var capture = CreateCapture(incidentTime);
        var stale = new DiagnosticArtifact(
            "WindowsErrorReportingEventLog",
            DiagnosticArtifactKind.WindowsErrorReport,
            @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue\Kernel_141",
            incidentTime,
            null,
            "old-report",
            "LiveKernelEvent",
            @"C:\Windows\LiveKernelReports\WATCHDOG\WATCHDOG-20260716-0341.dmp",
            "sig");

        var report = new IncidentReportBuilder().Build(
            capture,
            Array.Empty<DiagnosticEvent>(),
            new[] { stale });

        Assert.Single(report.Evidence);
        Assert.Equal(IncidentClassification.Unclassified, report.Classification);
    }

    [Fact]
    public void TryGetWatchdogSourceTimeUtc_ParsesWatchdogFilename()
    {
        var parsed = IncidentReportBuilder.TryGetWatchdogSourceTimeUtc(
            @"C:\Windows\LiveKernelReports\WATCHDOG\WATCHDOG-20260906-2320.dmp",
            out var sourceTime);

        Assert.True(parsed);
        Assert.Equal(TimeSpan.Zero, sourceTime.Offset);
    }

    private static IncidentCapture CreateCapture(
        DateTimeOffset incidentTime,
        params TelemetryFrame[] frames)
    {
        var trigger = new IncidentTrigger(
            Guid.NewGuid(),
            "test:evidence",
            "Diagnostic trigger observed",
            incidentTime,
            incidentTime,
            frames.Length == 0 ? 1 : frames[0].MonotonicTimestampTicks);

        return new IncidentCapture(trigger, frames);
    }

    private static ProcessObservation CreateProcessObservation(
        ProcessObservationState state,
        DateTimeOffset observedAtUtc)
    {
        var target = new ProcessInstance(
            new ProcessInstanceIdentity(4242, observedAtUtc.AddMinutes(-5)),
            "TestGame",
            @"C:\Games\TestGame.exe");

        return new ProcessObservation(
            state,
            target,
            observedAtUtc,
            null,
            null);
    }

    private static DiagnosticEvent CreateEvent(
        DiagnosticEventKind kind,
        string provider,
        int eventId,
        DateTimeOffset time) =>
        new(
            "WindowsEventLog",
            "System",
            provider,
            eventId,
            10,
            time,
            null,
            kind,
            "Error",
            "test");

    private static TelemetryFrame CreateFrame(
        long sequence,
        DateTimeOffset timestamp,
        double cpu = 10,
        double gpu = 20,
        double hotspot = 50,
        double vram = 1000,
        double memoryLoad = 40)
    {
        var system = new SystemTelemetry(
            MetricReading.Available(1000),
            MetricReading.Available(1000),
            MetricReading.Available(memoryLoad));
        var processor = new CpuTelemetry(
            MetricReading.Available(cpu),
            Array.Empty<LogicalProcessorLoad>(),
            MetricReading.Unsupported(),
            MetricReading.Unsupported(),
            MetricReading.Unsupported());
        var graphics = new GpuTelemetry(
            "/gpu/0",
            "GPU",
            MetricReading.Available(gpu),
            MetricReading.Available(10),
            MetricReading.Available(45),
            MetricReading.Available(hotspot),
            MetricReading.Unsupported(),
            MetricReading.Available(100),
            MetricReading.Available(2000),
            MetricReading.Available(2000),
            MetricReading.Available(vram),
            MetricReading.Available(16000),
            MetricReading.Available(0),
            MetricReading.Available(0));

        return new TelemetryFrame(
            sequence,
            timestamp,
            Math.Max(1, sequence + 1) * Stopwatch.Frequency,
            system,
            processor,
            new[] { graphics });
    }

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);
}
