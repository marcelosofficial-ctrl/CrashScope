using System.Diagnostics;
using System.Net.Http.Json;

namespace CrashScope.Agent.Runtime;

public sealed class ExistingCrashScopeInstanceProbe
{
    private readonly HttpClient _httpClient;

    public ExistingCrashScopeInstanceProbe(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<bool> IsRunningAsync(
        Uri dashboardUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dashboardUri);

        try
        {
            var metadataUri = new Uri(dashboardUri, "/api");
            var metadata = await _httpClient.GetFromJsonAsync<AgentMetadata>(
                metadataUri,
                cancellationToken);

            return metadata is not null
                && string.Equals(metadata.Name, "CrashScope.Agent", StringComparison.Ordinal)
                && string.Equals(metadata.Status, "running", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryOpenDashboard(Uri dashboardUri)
    {
        ArgumentNullException.ThrowIfNull(dashboardUri);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dashboardUri.AbsoluteUri,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed record AgentMetadata(string? Name, string? Status);
}