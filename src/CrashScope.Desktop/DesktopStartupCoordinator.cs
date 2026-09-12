using System.IO;
using System.Net.Http;
using System.Diagnostics;

namespace CrashScope.Desktop;

internal sealed class DesktopStartupCoordinator : IDisposable
{
    public static readonly Uri DashboardUri = new("http://localhost:5077/");

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly HttpClient _httpClient;
    private readonly AgentEndpointClient _endpoint;
    private bool _disposed;

    public DesktopStartupCoordinator()
    {
        _httpClient = new HttpClient
        {
            Timeout = ProbeTimeout
        };
        _endpoint = new AgentEndpointClient(_httpClient);
    }

    public async Task EnsureAgentAsync(CancellationToken cancellationToken = default)
    {
        if (await _endpoint.IsRunningAsync(DashboardUri, cancellationToken))
        {
            return;
        }

        var explicitPath = Environment.GetEnvironmentVariable(
            AgentExecutableResolver.AgentPathEnvironmentVariable);

        var agentPath = AgentExecutableResolver.Resolve(
            AppContext.BaseDirectory,
            explicitPath);

        if (agentPath is null)
        {
            throw new InvalidOperationException(
                "CrashScope Agent was not found beside the desktop application. "
                + "During development, CRASHSCOPE_AGENT_PATH can point to CrashScope.exe.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = agentPath,
            WorkingDirectory = Path.GetDirectoryName(agentPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--no-browser");

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "CrashScope Agent could not be started.",
                ex);
        }

        var deadline = DateTimeOffset.UtcNow + StartupTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _endpoint.IsRunningAsync(DashboardUri, cancellationToken))
            {
                return;
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        throw new TimeoutException(
            $"CrashScope Agent did not become ready at {DashboardUri}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}