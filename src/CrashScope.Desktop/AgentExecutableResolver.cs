using System.IO;
namespace CrashScope.Desktop;

internal static class AgentExecutableResolver
{
    internal const string AgentPathEnvironmentVariable = "CRASHSCOPE_AGENT_PATH";

    public static string? Resolve(
        string baseDirectory,
        string? explicitAgentPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        if (!string.IsNullOrWhiteSpace(explicitAgentPath)
            && File.Exists(explicitAgentPath))
        {
            return Path.GetFullPath(explicitAgentPath);
        }

        var sibling = Path.Combine(baseDirectory, "CrashScope.exe");
        if (File.Exists(sibling))
        {
            return Path.GetFullPath(sibling);
        }

        var parentDirectory = Directory.GetParent(
            Path.GetFullPath(baseDirectory));

        if (parentDirectory is null)
        {
            return null;
        }

        var packagedAgent = Path.Combine(
            parentDirectory.FullName,
            "CrashScope.exe");

        return File.Exists(packagedAgent)
            ? Path.GetFullPath(packagedAgent)
            : null;
    }
}