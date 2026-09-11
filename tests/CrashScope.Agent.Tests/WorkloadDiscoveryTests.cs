using CrashScope.Agent.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class WorkloadDiscoveryTests
{
    [Fact]
    public void Discover_ReturnsRankedAttachableProcessesAndExcludesAgent()
    {
        var discovery = new WorkloadDiscoveryService();

        var candidates = discovery.Discover(25);

        Assert.True(candidates.Count <= 25);
        Assert.DoesNotContain(candidates, x => x.ProcessId == Environment.ProcessId);
        Assert.All(candidates, candidate =>
        {
            Assert.True(candidate.ProcessId > 0);
            Assert.Equal(TimeSpan.Zero, candidate.ProcessStartTimeUtc.Offset);
            Assert.False(string.IsNullOrWhiteSpace(candidate.ProcessName));
            Assert.True(candidate.RecommendationScore > 0);
        });

        Assert.Equal(
            candidates.OrderByDescending(x => x.RecommendationScore)
                .ThenByDescending(x => x.WorkingSetMiB)
                .ThenBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.ProcessId),
            candidates.Select(x => x.ProcessId));
    }
}
