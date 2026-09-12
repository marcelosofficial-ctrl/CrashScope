namespace CrashScope.Desktop;

internal static class DashboardNavigationPolicy
{
    public static bool IsAllowed(string? rawUri)
    {
        if (string.IsNullOrWhiteSpace(rawUri))
        {
            return false;
        }

        if (string.Equals(rawUri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var loopback = string.Equals(
                uri.Host,
                "localhost",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                uri.Host,
                "127.0.0.1",
                StringComparison.OrdinalIgnoreCase);

        return loopback && uri.Port == 5077;
    }
}