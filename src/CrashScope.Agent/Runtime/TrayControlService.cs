using CrashScope.Agent.Sessions;
using CrashScope.Agent.Settings;

namespace CrashScope.Agent.Runtime;

internal sealed record TrayDisplayState(
    string StatusText,
    string ToolTip,
    bool AutoAssistEnabled,
    StartupRegistrationStatus Startup)
{
    public static TrayDisplayState Create(
        string? activeProcessName,
        bool autoAssistEnabled,
        StartupRegistrationStatus startup)
    {
        var monitoring = !string.IsNullOrWhiteSpace(activeProcessName);
        return new TrayDisplayState(
            monitoring ? $"Monitoring {activeProcessName}" : "Ready",
            monitoring ? $"CrashScope - Monitoring {activeProcessName}" : "CrashScope - Ready",
            autoAssistEnabled,
            startup);
    }
}

internal sealed class TrayControlService
{
    private static readonly Uri DashboardUri = new("http://localhost:5077/");

    private readonly WorkloadSessionManager _sessions;
    private readonly CrashScopeSettingsState _settings;
    private readonly WindowsStartupRegistration _startup;
    private readonly IHostApplicationLifetime _lifetime;

    public TrayControlService(
        WorkloadSessionManager sessions,
        CrashScopeSettingsState settings,
        WindowsStartupRegistration startup,
        IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(lifetime);

        _sessions = sessions;
        _settings = settings;
        _startup = startup;
        _lifetime = lifetime;
    }

    public TrayDisplayState Snapshot()
    {
        var active = _sessions.ActiveSnapshot();
        var settings = _settings.Snapshot();
        return TrayDisplayState.Create(
            active?.ProcessName,
            settings.AutoAssistEnabled,
            _startup.GetStatus());
    }

    public bool OpenDashboard() =>
        DesktopShellLauncher.TryOpen()
        || ExistingCrashScopeInstanceProbe.TryOpenDashboard(DashboardUri);

    public ValueTask<CrashScopeSettings> ToggleAutoAssistAsync(
        CancellationToken cancellationToken = default)
    {
        var current = _settings.Snapshot();
        return _settings.UpdateAutoAssistAsync(!current.AutoAssistEnabled, cancellationToken);
    }

    public StartupRegistrationStatus ToggleStartup()
    {
        var current = _startup.GetStatus();
        return current.Supported
            ? _startup.SetEnabled(!current.Enabled)
            : current;
    }

    public void Exit() => _lifetime.StopApplication();
}
