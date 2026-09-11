using CrashScope.Core.Diagnostics;

namespace CrashScope.Core.Tests;

public sealed class DiagnosticEventTests
{
    [Fact]
    public void PreservesObservationAndOptionalSourceTimesSeparately()
    {
        var observed = Utc(2026, 9, 8, 3, 0, 0);
        var occurred = Utc(2026, 9, 8, 2, 59, 55);

        var diagnosticEvent = new DiagnosticEvent(
            "WindowsEventLog",
            "Application",
            "Windows Error Reporting",
            1001,
            123,
            observed,
            occurred,
            DiagnosticEventKind.WindowsErrorReport,
            "Information",
            "fixture");

        Assert.Equal(observed, diagnosticEvent.ObservedAtUtc);
        Assert.Equal(occurred, diagnosticEvent.SourceOccurredAtUtc);
        Assert.NotEqual(
            diagnosticEvent.ObservedAtUtc,
            diagnosticEvent.SourceOccurredAtUtc);
    }

    [Fact]
    public void RejectsNonUtcObservationTime()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticEvent(
            "WindowsEventLog",
            "System",
            "Display",
            4101,
            1,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(9)),
            null,
            DiagnosticEventKind.DisplayDriver,
            null,
            null));
    }

    [Fact]
    public void RejectsNonUtcSourceOccurrenceTime()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticEvent(
            "WindowsEventLog",
            "System",
            "Display",
            4101,
            1,
            Utc(2026, 9, 8, 3, 0, 0),
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(9)),
            DiagnosticEventKind.DisplayDriver,
            null,
            null));
    }

    [Fact]
    public void NormalizesBlankOptionalTextToNull()
    {
        var diagnosticEvent = new DiagnosticEvent(
            "WindowsEventLog",
            "System",
            "Display",
            4101,
            1,
            Utc(2026, 9, 8, 3, 0, 0),
            null,
            DiagnosticEventKind.DisplayDriver,
            "   ",
            "");

        Assert.Null(diagnosticEvent.Level);
        Assert.Null(diagnosticEvent.Message);
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
