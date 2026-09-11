using CrashScope.Core.Sessions;

namespace CrashScope.Core.Tests;

public sealed class WorkloadSessionTests
{
    [Fact]
    public void ActiveSessionRequiresUtcIdentityAndNoEndReason()
    {
        var session = new WorkloadSession(
            Guid.NewGuid(),
            42,
            Utc(7, 0),
            "game",
            @"C:\Games\game.exe",
            Utc(7, 5),
            null,
            null,
            SessionTelemetrySummary.Empty);

        Assert.True(session.IsActive);
        Assert.Null(session.EndedAtUtc);
        Assert.Null(session.EndReason);
    }

    [Fact]
    public void EndedSessionRequiresMatchingEndReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkloadSession(
                Guid.NewGuid(),
                42,
                Utc(7, 0),
                "game",
                null,
                Utc(7, 5),
                Utc(7, 10),
                null,
                SessionTelemetrySummary.Empty));
    }

    [Fact]
    public void IncidentIdsAreDefensivelyDeduplicated()
    {
        var id = Guid.NewGuid();
        var source = new[] { id, id };
        var session = new WorkloadSession(
            Guid.NewGuid(),
            42,
            Utc(7, 0),
            "game",
            null,
            Utc(7, 5),
            null,
            null,
            SessionTelemetrySummary.Empty,
            source);

        Assert.Single(session.IncidentIds);
    }

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2026, 9, 8, hour, minute, 0, TimeSpan.Zero);
}
