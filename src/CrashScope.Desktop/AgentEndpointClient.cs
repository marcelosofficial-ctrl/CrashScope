using System.Net.Http;
using System.Net.Http.Json;

namespace CrashScope.Desktop;

internal sealed class AgentEndpointClient
{
    private readonly HttpClient _httpClient;

    public AgentEndpointClient(HttpClient httpClient)
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
            using var response = await _httpClient.GetAsync(metadataUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var metadata = await response.Content.ReadFromJsonAsync<AgentMetadata>(
                cancellationToken: cancellationToken);

            return metadata is not null
                && string.Equals(
                    metadata.Name,
                    "CrashScope.Agent",
                    StringComparison.Ordinal)
                && string.Equals(
                    metadata.Status,
                    "running",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or NotSupportedException
            or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private sealed record AgentMetadata(string? Name, string? Status);
}