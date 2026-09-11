using CrashScope.Agent.Runtime;

namespace CrashScope.Agent.Tests;

public sealed class CrashScopeLaunchOptionsTests
{
    [Fact]
    public void Parse_DefaultsToOpeningBrowser()
    {
        var options = CrashScopeLaunchOptions.Parse(Array.Empty<string>());

        Assert.True(options.OpenBrowser);
        Assert.Empty(options.HostArguments);
    }

    [Theory]
    [InlineData("--no-browser")]
    [InlineData("--NO-BROWSER")]
    public void Parse_DisablesBrowserAndRemovesCrashScopeArgument(string argument)
    {
        var options = CrashScopeLaunchOptions.Parse(new[] { argument });

        Assert.False(options.OpenBrowser);
        Assert.Empty(options.HostArguments);
    }

    [Fact]
    public void Parse_PreservesHostArguments()
    {
        var options = CrashScopeLaunchOptions.Parse(new[]
        {
            "--no-browser",
            "--environment",
            "Development"
        });

        Assert.False(options.OpenBrowser);
        Assert.Equal(new[] { "--environment", "Development" }, options.HostArguments);
    }
}
