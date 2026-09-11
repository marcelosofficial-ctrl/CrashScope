using CrashScope.Core.Diagnostics;

namespace CrashScope.Core.Tests;

public sealed class DiagnosticArtifactTests
{
    [Fact]
    public void Constructor_PreservesStructuredIdentityAndDefensivelyCopiesProperties()
    {
        var properties = new Dictionary<string, string>
        {
            ["EventType"] = "LiveKernelEvent"
        };
        var observed = new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero);
        var occurred = observed.AddSeconds(-2);

        var artifact = new DiagnosticArtifact(
            "WindowsErrorReporting",
            DiagnosticArtifactKind.WindowsErrorReport,
            @"C:\WER\Report.wer",
            observed,
            occurred,
            "report-1",
            "LiveKernelEvent",
            @"C:\Windows\LiveKernelReports\WATCHDOG\watchdog.dmp",
            "ABC123",
            properties);

        properties["EventType"] = "changed";

        Assert.Equal("report-1", artifact.ReportId);
        Assert.Equal("LiveKernelEvent", artifact.EventType);
        Assert.Equal(occurred, artifact.SourceOccurredAtUtc);
        Assert.Equal("LiveKernelEvent", artifact.Properties["EventType"]);
    }

    [Fact]
    public void Constructor_RejectsNonUtcObservationTime()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticArtifact(
            "source",
            DiagnosticArtifactKind.Other,
            "artifact",
            new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.FromHours(9)),
            null,
            null,
            null,
            null,
            null));
    }
}
