using CrashScope.Core.Diagnostics;
using CrashScope.Infrastructure.Diagnostics;

namespace CrashScope.Infrastructure.Tests;

public sealed class WindowsEventLogDiagnosticSourceTests
{
    [Theory]
    [InlineData("Application Error", DiagnosticEventKind.ApplicationFault)]
    [InlineData("Application Hang", DiagnosticEventKind.ApplicationHang)]
    [InlineData("Windows Error Reporting", DiagnosticEventKind.WindowsErrorReport)]
    [InlineData("Microsoft-Windows-WHEA-Logger", DiagnosticEventKind.HardwareError)]
    [InlineData("Microsoft-Windows-Kernel-Power", DiagnosticEventKind.KernelPower)]
    [InlineData("Display", DiagnosticEventKind.DisplayDriver)]
    [InlineData("Unknown Provider", DiagnosticEventKind.Other)]
    public void MapsValidatedProvidersToEvidenceKinds(
        string provider,
        DiagnosticEventKind expected)
    {
        Assert.Equal(expected, WindowsEventLogMapper.MapKind(provider));
    }

    [Fact]
    public void MapperPreservesRecordIdentityAndDoesNotInventSourceTime()
    {
        var observed = new DateTimeOffset(
            2026,
            9,
            8,
            3,
            0,
            0,
            TimeSpan.Zero);

        var snapshot = new WindowsEventRecordSnapshot(
            "Application",
            "Windows Error Reporting",
            1001,
            987654,
            observed,
            "Information",
            "LiveKernelEvent 193 fixture");

        var diagnosticEvent = WindowsEventLogMapper.Map(snapshot);

        Assert.Equal("WindowsEventLog", diagnosticEvent.Source);
        Assert.Equal("Application", diagnosticEvent.LogName);
        Assert.Equal(1001, diagnosticEvent.EventId);
        Assert.Equal(987654, diagnosticEvent.RecordId);
        Assert.Equal(observed, diagnosticEvent.ObservedAtUtc);
        Assert.Null(diagnosticEvent.SourceOccurredAtUtc);
        Assert.Equal(
            DiagnosticEventKind.WindowsErrorReport,
            diagnosticEvent.Kind);
    }

    [Fact]
    public void ProviderQueryIncludesAllProvidersWithoutBroadWildcardMatching()
    {
        var query = WindowsEventLogDiagnosticSource.BuildProviderQuery(new[]
        {
            "Application Error",
            "Windows Error Reporting",
            "Application Hang"
        });

        Assert.Contains("Provider[@Name='Application Error']", query);
        Assert.Contains("Provider[@Name='Windows Error Reporting']", query);
        Assert.Contains("Provider[@Name='Application Hang']", query);
        Assert.DoesNotContain("contains(", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReaderRejectsNonUtcBoundaryBeforeTouchingEventLog()
    {
        var source = new WindowsEventLogDiagnosticSource();
        var nonUtc = new DateTimeOffset(
            2026,
            9,
            8,
            12,
            0,
            0,
            TimeSpan.FromHours(9));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.ReadSinceAsync(nonUtc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReaderRejectsInvalidMaximumEventCount(int maximumEvents)
    {
        var source = new WindowsEventLogDiagnosticSource();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await source.ReadSinceAsync(DateTimeOffset.UtcNow, maximumEvents));
    }
}
