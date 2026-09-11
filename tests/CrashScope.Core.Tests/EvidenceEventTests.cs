using CrashScope.Core.Evidence;

namespace CrashScope.Core.Tests;

public sealed class EvidenceEventTests
{
    [Fact]
    public void ConstructorDefensivelyCopiesDetails()
    {
        var details = new Dictionary<string, string>
        {
            ["setting"] = "Renderer",
            ["before"] = "DX11"
        };

        var item = new EvidenceEvent(
            Utc(10, 0),
            Utc(10, 0, 1),
            "ConfigTrace",
            "config.change",
            EvidenceSeverity.Information,
            "Renderer changed.",
            details);

        details["before"] = "mutated";
        details["after"] = "DX12";

        Assert.Equal("DX11", item.Details["before"]);
        Assert.False(item.Details.ContainsKey("after"));
    }

    [Fact]
    public void RejectsNonUtcTimestamp()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceEvent(
            new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.FromHours(9)),
            Utc(10, 0),
            "ConfigTrace",
            "config.change",
            EvidenceSeverity.Information,
            "Renderer changed."));
    }

    [Fact]
    public void RejectsNonUtcObservationTime()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceEvent(
            Utc(10, 0),
            new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.FromHours(9)),
            "ConfigTrace",
            "config.change",
            EvidenceSeverity.Information,
            "Renderer changed."));
    }

    [Fact]
    public void RejectsBlankSource()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceEvent(
            Utc(10, 0),
            Utc(10, 0),
            " ",
            "config.change",
            EvidenceSeverity.Information,
            "Renderer changed."));
    }

    [Fact]
    public void RejectsBlankKind()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceEvent(
            Utc(10, 0),
            Utc(10, 0),
            "ConfigTrace",
            " ",
            EvidenceSeverity.Information,
            "Renderer changed."));
    }

    [Fact]
    public void RejectsBlankSummary()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceEvent(
            Utc(10, 0),
            Utc(10, 0),
            "ConfigTrace",
            "config.change",
            EvidenceSeverity.Information,
            " "));
    }

    [Fact]
    public void RejectsUnknownSeverity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvidenceEvent(
            Utc(10, 0),
            Utc(10, 0),
            "ConfigTrace",
            "config.change",
            (EvidenceSeverity)99,
            "Renderer changed."));
    }

    private static DateTimeOffset Utc(int hour, int minute, int second = 0) =>
        new(2026, 9, 11, hour, minute, second, TimeSpan.Zero);
}