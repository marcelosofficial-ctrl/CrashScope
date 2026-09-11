using CrashScope.Agent.Evidence.ConfigTrace;

namespace CrashScope.Agent.Tests;

public sealed class ConfigTraceRuntimePathsTests
{
    [Fact]
    public void ExecutableResolvesUnderPackagedProviderDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var path = ConfigTraceRuntimePaths.ResolveExecutablePath(root);

        Assert.Equal(
            Path.Combine(root, "providers", "ConfigTrace", "configtrace.exe"),
            path);
    }

    [Fact]
    public void JournalDirectoryResolvesUnderCrashScopeOwnedEvidenceDirectory()
    {
        var productDataRoot = Path.Combine(
            Path.GetTempPath(),
            "CrashScope",
            Guid.NewGuid().ToString("N"));

        var path = ConfigTraceRuntimePaths.ResolveJournalDirectory(productDataRoot);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(productDataRoot), "evidence", "ConfigTrace"),
            path);
    }
}