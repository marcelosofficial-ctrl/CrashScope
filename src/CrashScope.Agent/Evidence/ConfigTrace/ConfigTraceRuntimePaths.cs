namespace CrashScope.Agent.Evidence.ConfigTrace;

internal static class ConfigTraceRuntimePaths
{
    internal const string ProvidersDirectoryName = "providers";
    internal const string ProviderDirectoryName = "ConfigTrace";
    internal const string ExecutableFileName = "configtrace.exe";

    public static string ResolveExecutablePath(string? baseDirectory = null)
    {
        var root = Path.GetFullPath(
            string.IsNullOrWhiteSpace(baseDirectory)
                ? AppContext.BaseDirectory
                : baseDirectory);

        return Path.Combine(
            root,
            ProvidersDirectoryName,
            ProviderDirectoryName,
            ExecutableFileName);
    }

    public static string ResolveJournalDirectory(string productDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productDataRoot);

        return Path.Combine(
            Path.GetFullPath(productDataRoot),
            "evidence",
            ProviderDirectoryName);
    }
}