using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CrashScope.Agent.Runtime;

internal interface IUserStartupStore
{
    string? Read(string name);
    void Write(string name, string command);
    void Delete(string name);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCurrentUserRunStore : IUserStartupStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(name) as string;
    }

    public void Write(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current-user Windows startup registry key.");
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

internal sealed record StartupRegistrationStatus(
    bool Supported,
    bool Enabled,
    string? Detail);

internal sealed class WindowsStartupRegistration
{
    internal const string ValueName = "CrashScope";

    private readonly IUserStartupStore? _store;
    private readonly string _executablePath;

    public WindowsStartupRegistration(
        string executablePath,
        IUserStartupStore? store = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = Path.GetFullPath(executablePath);
        _store = store ?? CreatePlatformStore();
    }

    public StartupRegistrationStatus GetStatus()
    {
        if (_store is null)
        {
            return new StartupRegistrationStatus(
                Supported: false,
                Enabled: false,
                Detail: "Start with Windows is available only on Windows.");
        }

        try
        {
            var current = _store.Read(ValueName);
            var expected = BuildCommand(_executablePath);
            return new StartupRegistrationStatus(
                Supported: true,
                Enabled: string.Equals(current, expected, StringComparison.Ordinal),
                Detail: current is null
                    ? null
                    : string.Equals(current, expected, StringComparison.Ordinal)
                        ? null
                        : "A different CrashScope startup command is registered and will not be overwritten until you enable this setting again.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new StartupRegistrationStatus(
                Supported: true,
                Enabled: false,
                Detail: "Windows did not allow CrashScope to read the current-user startup setting.");
        }
    }

    public StartupRegistrationStatus SetEnabled(bool enabled)
    {
        if (_store is null)
        {
            return GetStatus();
        }

        try
        {
            if (enabled)
            {
                _store.Write(ValueName, BuildCommand(_executablePath));
            }
            else
            {
                _store.Delete(ValueName);
            }

            return GetStatus();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new StartupRegistrationStatus(
                Supported: true,
                Enabled: false,
                Detail: enabled
                    ? "Windows did not allow CrashScope to register current-user startup."
                    : "Windows did not allow CrashScope to remove its current-user startup entry.");
        }
    }

    internal static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{Path.GetFullPath(executablePath)}\" --no-browser";
    }

    private static IUserStartupStore? CreatePlatformStore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return CreateWindowsStore();
    }

    [SupportedOSPlatform("windows")]
    private static IUserStartupStore CreateWindowsStore() =>
        new WindowsCurrentUserRunStore();
}
