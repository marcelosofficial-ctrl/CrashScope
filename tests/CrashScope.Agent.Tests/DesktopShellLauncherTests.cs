using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CrashScope.Agent.Runtime;

namespace CrashScope.Agent.Tests;

public sealed class DesktopShellLauncherTests
{
    [Fact]
    public void ResolveExecutable_PrefersExplicitExistingPath()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var desktopDirectory = Path.Combine(
                root,
                DesktopShellLauncher.DesktopDirectoryName);
            Directory.CreateDirectory(desktopDirectory);

            var packagedDesktop = Path.Combine(
                desktopDirectory,
                DesktopShellLauncher.DesktopExecutableName);
            var explicitPath = Path.Combine(root, "CustomDesktop.exe");

            File.WriteAllText(packagedDesktop, "packaged");
            File.WriteAllText(explicitPath, "explicit");

            var actual = DesktopShellLauncher.ResolveExecutable(
                root,
                explicitPath);

            Assert.Equal(Path.GetFullPath(explicitPath), actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveExecutable_UsesPackagedDesktopSubdirectory()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var desktopDirectory = Path.Combine(
                root,
                DesktopShellLauncher.DesktopDirectoryName);
            Directory.CreateDirectory(desktopDirectory);

            var packagedDesktop = Path.Combine(
                desktopDirectory,
                DesktopShellLauncher.DesktopExecutableName);
            File.WriteAllText(packagedDesktop, "desktop");

            var actual = DesktopShellLauncher.ResolveExecutable(
                root,
                explicitPath: null);

            Assert.Equal(Path.GetFullPath(packagedDesktop), actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveExecutable_ReturnsNullWhenShellIsMissing()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            Assert.Null(
                DesktopShellLauncher.ResolveExecutable(
                    root,
                    explicitPath: null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryOpen_UsesShellExecutionForPackagedDesktop()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var desktopDirectory = Path.Combine(
                root,
                DesktopShellLauncher.DesktopDirectoryName);
            Directory.CreateDirectory(desktopDirectory);

            var packagedDesktop = Path.Combine(
                desktopDirectory,
                DesktopShellLauncher.DesktopExecutableName);
            File.WriteAllText(packagedDesktop, "desktop");

            ProcessStartInfo? captured = null;

            var opened = DesktopShellLauncher.TryOpen(
                root,
                explicitPath: null,
                startProcess: info =>
                {
                    captured = info;
                    return null;
                });

            Assert.True(opened);
            Assert.NotNull(captured);
            Assert.Equal(
                Path.GetFullPath(packagedDesktop),
                captured!.FileName);
            Assert.Equal(
                Path.GetFullPath(desktopDirectory),
                captured.WorkingDirectory);
            Assert.True(captured.UseShellExecute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryOpen_UsesExplicitDesktopAndItsOwnWorkingDirectory()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var customDirectory = Path.Combine(root, "custom");
            Directory.CreateDirectory(customDirectory);

            var explicitPath = Path.Combine(
                customDirectory,
                "CrashScope.CustomDesktop.exe");
            File.WriteAllText(explicitPath, "desktop");

            ProcessStartInfo? captured = null;

            var opened = DesktopShellLauncher.TryOpen(
                root,
                explicitPath,
                startProcess: info =>
                {
                    captured = info;
                    return null;
                });

            Assert.True(opened);
            Assert.NotNull(captured);
            Assert.Equal(
                Path.GetFullPath(explicitPath),
                captured!.FileName);
            Assert.Equal(
                Path.GetFullPath(customDirectory),
                captured.WorkingDirectory);
            Assert.True(captured.UseShellExecute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryOpen_ReturnsFalseWithoutCallingStarterWhenDesktopIsMissing()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var starterCalled = false;

            var opened = DesktopShellLauncher.TryOpen(
                root,
                explicitPath: null,
                startProcess: _ =>
                {
                    starterCalled = true;
                    return null;
                });

            Assert.False(opened);
            Assert.False(starterCalled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryOpen_ReturnsFalseWhenDesktopProcessCannotStart()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var desktopDirectory = Path.Combine(
                root,
                DesktopShellLauncher.DesktopDirectoryName);
            Directory.CreateDirectory(desktopDirectory);

            var packagedDesktop = Path.Combine(
                desktopDirectory,
                DesktopShellLauncher.DesktopExecutableName);
            File.WriteAllText(packagedDesktop, "desktop");

            var opened = DesktopShellLauncher.TryOpen(
                root,
                explicitPath: null,
                startProcess: _ => throw new Win32Exception("synthetic"));

            Assert.False(opened);
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
            "CrashScope.Agent.Tests",
            "DesktopShellLauncher",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }
}