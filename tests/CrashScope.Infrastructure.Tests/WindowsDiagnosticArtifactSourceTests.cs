using CrashScope.Core.Diagnostics;
using CrashScope.Infrastructure.Diagnostics;

namespace CrashScope.Infrastructure.Tests;

public sealed class WindowsDiagnosticArtifactSourceTests
{
    [Fact]
    public async Task ReadSinceAsync_ParsesWerReportAndDiscoversLiveKernelDump()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CrashScope-{Guid.NewGuid():N}");
        var archive = Path.Combine(root, "ReportArchive");
        var reportDirectory = Path.Combine(archive, "Kernel_193");
        var liveKernel = Path.Combine(root, "LiveKernelReports", "WATCHDOG");
        Directory.CreateDirectory(reportDirectory);
        Directory.CreateDirectory(liveKernel);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var eventTime = now.AddSeconds(-3);
            var dumpPath = Path.Combine(liveKernel, "WATCHDOG-20260831-1649.dmp");
            await File.WriteAllBytesAsync(dumpPath, new byte[] { 1, 2, 3 });
            File.SetLastWriteTimeUtc(dumpPath, now.UtcDateTime);

            var reportPath = Path.Combine(reportDirectory, "Report.wer");
            var reportId = Guid.NewGuid().ToString("D");
            var contents = string.Join(
                Environment.NewLine,
                "Version=1",
                "EventType=LiveKernelEvent",
                $"EventTime={eventTime.UtcDateTime.ToFileTimeUtc()}",
                $"ReportIdentifier={reportId}",
                "Sig[0].Name=Problem Signature 01",
                "Sig[0].Value=193",
                "Sig[1].Name=Problem Signature 02",
                "Sig[1].Value=80e",
                $"DumpFile={dumpPath}");

            await File.WriteAllTextAsync(reportPath, contents);
            File.SetLastWriteTimeUtc(reportPath, now.UtcDateTime);

            var source = new WindowsDiagnosticArtifactSource(
                new[] { archive },
                Path.Combine(root, "LiveKernelReports"));

            var artifacts = await source.ReadSinceAsync(now.AddMinutes(-1));

            Assert.Equal(2, artifacts.Count);

            var wer = Assert.Single(
                artifacts,
                x => x.Kind == DiagnosticArtifactKind.WindowsErrorReport);
            Assert.Equal(reportId, wer.ReportId);
            Assert.Equal("LiveKernelEvent", wer.EventType);
            Assert.Equal(dumpPath, wer.RelatedPath);
            Assert.NotNull(wer.Signature);
            Assert.Equal(eventTime.ToUnixTimeSeconds(),
                wer.SourceOccurredAtUtc!.Value.ToUnixTimeSeconds());

            var dump = Assert.Single(
                artifacts,
                x => x.Kind == DiagnosticArtifactKind.LiveKernelDump);
            Assert.Equal(dumpPath, dump.ArtifactPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadSinceAsync_UsesWerEventLogFallbackWhenFilesAreUnavailable()
    {
        var observed = new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero);
        const string reportId = "01234567-89ab-cdef-0123-456789abcdef";
        const string dumpPath = @"C:\Windows\LiveKernelReports\WATCHDOG\WATCHDOG-20260831-1649.dmp";
        const string reportPath = @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive\Kernel_193\Report.wer";
        var message = $"""
            Problem signature:
            P1: 193
            P2: 80e

            Event Name: LiveKernelEvent
            These files may be available here:
            {reportPath}
            {dumpPath}
            Report Id: {reportId}
            """;

        var fallback = new StubDiagnosticEventSource(
            new DiagnosticEvent(
                "WindowsEventLog",
                "Application",
                "Windows Error Reporting",
                1001,
                1234,
                observed,
                null,
                DiagnosticEventKind.WindowsErrorReport,
                "Information",
                message));

        var source = new WindowsDiagnosticArtifactSource(
            Array.Empty<string>(),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            fallback);

        var artifacts = await source.ReadSinceAsync(observed.AddMinutes(-1));

        var artifact = Assert.Single(artifacts);
        Assert.Equal("WindowsErrorReportingEventLog", artifact.Source);
        Assert.Equal(DiagnosticArtifactKind.WindowsErrorReport, artifact.Kind);
        Assert.Equal(reportId, artifact.ReportId);
        Assert.Equal("LiveKernelEvent", artifact.EventType);
        Assert.Equal(dumpPath, artifact.RelatedPath);
        Assert.Equal(reportPath, artifact.ArtifactPath);
        Assert.NotNull(artifact.Signature);
        Assert.Equal("WindowsEventLog", artifact.Properties["EvidenceOrigin"]);
        Assert.Equal("1234", artifact.Properties["EventRecordId"]);
    }

    [Fact]
    public void MapWerEventArtifact_IgnoresUnrelatedEvents()
    {
        var diagnosticEvent = new DiagnosticEvent(
            "WindowsEventLog",
            "System",
            "Microsoft-Windows-Kernel-Power",
            41,
            1,
            new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero),
            null,
            DiagnosticEventKind.KernelPower,
            "Critical",
            "System rebooted unexpectedly.");

        Assert.Null(WindowsDiagnosticArtifactSource.MapWerEventArtifact(diagnosticEvent));
    }

    [Fact]
    public async Task ReadSinceAsync_RejectsNonUtcBoundary()
    {
        var source = new WindowsDiagnosticArtifactSource(
            Array.Empty<string>(),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.ReadSinceAsync(
                new DateTimeOffset(
                    2026, 9, 8, 4, 0, 0, TimeSpan.FromHours(9))));
    }

    [Fact]
    public async Task ReadSinceAsync_HonorsMaximumArtifactCount()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CrashScope-{Guid.NewGuid():N}");
        var liveKernel = Path.Combine(root, "LiveKernelReports");
        Directory.CreateDirectory(liveKernel);

        try
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                var path = Path.Combine(liveKernel, $"dump-{i}.dmp");
                await File.WriteAllBytesAsync(path, new byte[] { (byte)i });
                File.SetLastWriteTimeUtc(path, now.AddSeconds(i).UtcDateTime);
            }

            var source = new WindowsDiagnosticArtifactSource(
                Array.Empty<string>(),
                liveKernel);

            var artifacts = await source.ReadSinceAsync(
                now.AddMinutes(-1),
                maximumArtifacts: 2);

            Assert.Equal(2, artifacts.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubDiagnosticEventSource : IDiagnosticEventSource
    {
        private readonly IReadOnlyList<DiagnosticEvent> _events;

        public StubDiagnosticEventSource(params DiagnosticEvent[] events)
        {
            _events = events;
        }

        public string Name => "Stub";

        public ValueTask<IReadOnlyList<DiagnosticEvent>> ReadSinceAsync(
            DateTimeOffset sinceUtc,
            int maximumEvents = 256,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DiagnosticEvent> result = _events
                .Where(x => x.ObservedAtUtc >= sinceUtc)
                .Take(maximumEvents)
                .ToArray();

            return ValueTask.FromResult(result);
        }
    }
}
