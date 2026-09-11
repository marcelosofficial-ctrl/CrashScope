using CrashScope.Core.Evidence;

namespace CrashScope.Core.Tests;

public sealed class EvidenceProviderContractTests
{
    [Fact]
    public void SessionContextRejectsEmptySessionId()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceProviderSessionContext(
            Guid.Empty,
            Utc()));
    }

    [Fact]
    public void SessionContextRejectsNonUtcStart()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceProviderSessionContext(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.FromHours(9))));
    }

    [Fact]
    public void SessionContextRejectsInvalidProcessId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvidenceProviderSessionContext(
            Guid.NewGuid(),
            Utc(),
            processId: 0));
    }

    private static DateTimeOffset Utc() =>
        new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
}