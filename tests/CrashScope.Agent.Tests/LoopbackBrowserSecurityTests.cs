using CrashScope.Agent.Security;
using Microsoft.AspNetCore.Http;

namespace CrashScope.Agent.Tests;

public sealed class LoopbackBrowserSecurityTests
{
    private readonly LoopbackBrowserOriginPolicy _policy = new(5077);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowed_AllowsRequestsWithoutBrowserOrigin(string? origin)
    {
        Assert.True(_policy.IsAllowed(origin));
    }

    [Theory]
    [InlineData("http://localhost:5077")]
    [InlineData("http://LOCALHOST:5077")]
    [InlineData("http://127.0.0.1:5077")]
    [InlineData("http://[::1]:5077")]
    public void IsAllowed_AllowsCrashScopeLoopbackOrigins(string origin)
    {
        Assert.True(_policy.IsAllowed(origin));
    }

    [Theory]
    [InlineData("https://localhost:5077")]
    [InlineData("http://localhost:5078")]
    [InlineData("http://example.com:5077")]
    [InlineData("http://localhost.example.com:5077")]
    [InlineData("null")]
    [InlineData("not-a-uri")]
    public void IsAllowed_RejectsNonCrashScopeBrowserOrigins(string origin)
    {
        Assert.False(_policy.IsAllowed(origin));
    }

    [Fact]
    public void Constructor_RejectsInvalidPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoopbackBrowserOriginPolicy(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoopbackBrowserOriginPolicy(65536));
    }

    [Fact]
    public void BrowserSecurityHeaders_ApplyExpectedHardeningHeaders()
    {
        var context = new DefaultHttpContext();

        BrowserSecurityHeaders.Apply(context.Response.Headers, 5077);

        Assert.Equal("no-store", context.Response.Headers["Cache-Control"]);
        Assert.Equal("no-cache", context.Response.Headers["Pragma"]);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("camera=(), microphone=(), geolocation=()", context.Response.Headers["Permissions-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Resource-Policy"]);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("ws://localhost:5077", csp, StringComparison.Ordinal);
        Assert.Contains("ws://127.0.0.1:5077", csp, StringComparison.Ordinal);
        Assert.Contains("ws://[::1]:5077", csp, StringComparison.Ordinal);
    }
}
