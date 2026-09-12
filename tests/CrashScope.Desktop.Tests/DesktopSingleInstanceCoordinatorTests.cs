using CrashScope.Desktop;

namespace CrashScope.Desktop.Tests;

public sealed class DesktopSingleInstanceCoordinatorTests
{
    [Fact]
    public void SameScope_AllowsOnlyOnePrimary()
    {
        var scope = CreateScope();

        using var first = new DesktopSingleInstanceCoordinator(scope);
        using var second = new DesktopSingleInstanceCoordinator(scope);

        Assert.True(first.TryAcquirePrimary());
        Assert.False(second.TryAcquirePrimary());
    }

    [Fact]
    public void SignalPrimary_NotifiesPrimaryListener()
    {
        var scope = CreateScope();

        using var primary = new DesktopSingleInstanceCoordinator(scope);
        using var secondary = new DesktopSingleInstanceCoordinator(scope);
        using var activated = new ManualResetEventSlim(false);

        Assert.True(primary.TryAcquirePrimary());
        Assert.False(secondary.TryAcquirePrimary());

        primary.StartListening(() => activated.Set());
        secondary.SignalPrimary();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void DifferentScopes_CanBothBecomePrimary()
    {
        using var first = new DesktopSingleInstanceCoordinator(CreateScope());
        using var second = new DesktopSingleInstanceCoordinator(CreateScope());

        Assert.True(first.TryAcquirePrimary());
        Assert.True(second.TryAcquirePrimary());
    }

    private static string CreateScope() =>
        $"CrashScope.Desktop.Tests.{Guid.NewGuid():N}";
}