using CrashScope.Desktop;

namespace CrashScope.Desktop.Tests;

public sealed class AgentExecutableResolverTests
{
    [Fact]
    public void Resolve_PrefersExplicitExistingAgentPath()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var sibling = Path.Combine(root, "CrashScope.exe");
            var explicitAgent = Path.Combine(root, "CustomCrashScope.exe");
            File.WriteAllText(sibling, "sibling");
            File.WriteAllText(explicitAgent, "explicit");

            var actual = AgentExecutableResolver.Resolve(root, explicitAgent);

            Assert.Equal(Path.GetFullPath(explicitAgent), actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_UsesSiblingAgentWhenExplicitPathIsMissing()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var sibling = Path.Combine(root, "CrashScope.exe");
            File.WriteAllText(sibling, "sibling");

            var actual = AgentExecutableResolver.Resolve(
                root,
                Path.Combine(root, "missing.exe"));

            Assert.Equal(Path.GetFullPath(sibling), actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_UsesParentAgentForPackagedDesktopSubdirectory()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var desktopDirectory = Path.Combine(root, "desktop");
            Directory.CreateDirectory(desktopDirectory);

            var packagedAgent = Path.Combine(root, "CrashScope.exe");
            File.WriteAllText(packagedAgent, "agent");

            var actual = AgentExecutableResolver.Resolve(desktopDirectory);

            Assert.Equal(Path.GetFullPath(packagedAgent), actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Resolve_ReturnsNullWhenNoAgentExists()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            Assert.Null(AgentExecutableResolver.Resolve(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "CrashScope.Desktop.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }
}