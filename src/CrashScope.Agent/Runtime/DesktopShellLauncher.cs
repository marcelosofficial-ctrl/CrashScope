using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CrashScope.Agent.Runtime;

internal static class DesktopShellLauncher
{
    internal const string DesktopPathEnvironmentVariable = "CRASHSCOPE_DESKTOP_PATH";
    internal const string DesktopDirectoryName = "desktop";
    internal const string DesktopExecutableName = "CrashScope.Desktop.exe";

    public static bool TryOpen()
    {
        var explicitPath = Environment.GetEnvironmentVariable(
            DesktopPathEnvironmentVariable);

        return TryOpen(
            AppContext.BaseDirectory,
            explicitPath,
            startProcess: null);
    }

    internal static bool TryOpen(
        string baseDirectory,
        string? explicitPath,
        Func<ProcessStartInfo, Process?>? startProcess)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var executable = ResolveExecutable(baseDirectory, explicitPath);
        if (executable is null)
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)
                    ?? Path.GetFullPath(baseDirectory),
                UseShellExecute = true
            };

            var starter = startProcess ?? Process.Start;
            starter(startInfo);
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or Win32Exception)
        {
            return false;
        }
    }

    internal static string? ResolveExecutable(
        string baseDirectory,
        string? explicitPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        if (!string.IsNullOrWhiteSpace(explicitPath)
            && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var packagedDesktop = Path.Combine(
            baseDirectory,
            DesktopDirectoryName,
            DesktopExecutableName);

        return File.Exists(packagedDesktop)
            ? Path.GetFullPath(packagedDesktop)
            : null;
    }
}