using System.Net;
using System.Text;
using CrashScope.Desktop;

namespace CrashScope.Desktop.Tests;

public sealed class AgentEndpointClientTests
{
    [Fact]
    public async Task IsRunningAsync_ReturnsTrueForCrashScopeAgentMetadata()
    {
        using var client = new HttpClient(new StubHandler(
            HttpStatusCode.OK,
            """{"name":"CrashScope.Agent","status":"running"}"""));
        var probe = new AgentEndpointClient(client);

        var running = await probe.IsRunningAsync(new Uri("http://localhost:5077/"));

        Assert.True(running);
    }

    [Fact]
    public async Task IsRunningAsync_ReturnsFalseForUnexpectedService()
    {
        using var client = new HttpClient(new StubHandler(
            HttpStatusCode.OK,
            """{"name":"SomethingElse","status":"running"}"""));
        var probe = new AgentEndpointClient(client);

        var running = await probe.IsRunningAsync(new Uri("http://localhost:5077/"));

        Assert.False(running);
    }

    [Fact]
    public async Task IsRunningAsync_ReturnsFalseForFailureStatus()
    {
        using var client = new HttpClient(new StubHandler(
            HttpStatusCode.ServiceUnavailable,
            """{"name":"CrashScope.Agent","status":"starting"}"""));
        var probe = new AgentEndpointClient(client);

        var running = await probe.IsRunningAsync(new Uri("http://localhost:5077/"));

        Assert.False(running);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _body,
                    Encoding.UTF8,
                    "application/json")
            };

            return Task.FromResult(response);
        }
    }
}