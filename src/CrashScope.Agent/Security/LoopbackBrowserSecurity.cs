using Microsoft.AspNetCore.Http;

namespace CrashScope.Agent.Security;

public sealed class LoopbackBrowserOriginPolicy
{
    private readonly int _port;

    public LoopbackBrowserOriginPolicy(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _port = port;
    }

    /// <summary>
    /// Requests made by native/local tooling commonly omit Origin and remain allowed.
    /// Browser-originated requests must come from CrashScope's own loopback origin.
    /// </summary>
    public bool IsAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Port != _port
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var host = uri.Host.Trim('[', ']');
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal);
    }
}

public static class BrowserSecurityHeaders
{
    public static void Apply(IHeaderDictionary headers, int port)
    {
        ArgumentNullException.ThrowIfNull(headers);

        // CrashScope serves local diagnostic state that may include process names,
        // paths and incident metadata. Avoid leaving those responses in browser caches.
        // Applying this to the bundled static UI also prevents stale assets after an
        // in-place portable update; the UI is local and small, so the cost is negligible.
        headers["Cache-Control"] = "no-store";
        headers["Pragma"] = "no-cache";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Content-Security-Policy"] = string.Join(' ',
            "default-src 'self';",
            "base-uri 'none';",
            "frame-ancestors 'none';",
            "form-action 'none';",
            "object-src 'none';",
            "img-src 'self' data:;",
            "font-src 'self';",
            "style-src 'self';",
            "script-src 'self';",
            $"connect-src 'self' ws://localhost:{port} ws://127.0.0.1:{port} ws://[::1]:{port};");
    }
}
