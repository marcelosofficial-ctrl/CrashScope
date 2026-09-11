namespace CrashScope.Agent.Runtime;

public sealed record CrashScopeLaunchOptions(
    bool OpenBrowser,
    string[] HostArguments)
{
    private const string NoBrowserArgument = "--no-browser";

    public static CrashScopeLaunchOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var openBrowser = true;
        var hostArguments = new List<string>();

        foreach (var argument in arguments)
        {
            if (string.Equals(argument, NoBrowserArgument, StringComparison.OrdinalIgnoreCase))
            {
                openBrowser = false;
                continue;
            }

            hostArguments.Add(argument);
        }

        return new CrashScopeLaunchOptions(openBrowser, hostArguments.ToArray());
    }
}
