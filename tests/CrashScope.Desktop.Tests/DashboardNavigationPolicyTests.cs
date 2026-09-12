using CrashScope.Desktop;

namespace CrashScope.Desktop.Tests;

public sealed class DashboardNavigationPolicyTests
{
    [Theory]
    [InlineData("http://localhost:5077/", true)]
    [InlineData("http://localhost:5077/incidents", true)]
    [InlineData("http://127.0.0.1:5077/api/status", true)]
    [InlineData("http://localhost:5078/", false)]
    [InlineData("https://localhost:5077/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("file:///C:/Windows/System32/notepad.exe", false)]
    public void IsAllowed_RestrictsNavigationToCrashScopeLoopback(
        string uri,
        bool expected)
    {
        Assert.Equal(expected, DashboardNavigationPolicy.IsAllowed(uri));
    }

    [Fact]
    public void IsAllowed_AllowsWebViewBootstrapBlankPage()
    {
        Assert.True(DashboardNavigationPolicy.IsAllowed("about:blank"));
    }
}