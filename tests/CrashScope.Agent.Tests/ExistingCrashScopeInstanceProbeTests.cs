using System.Net;
using System.Net.Http;
using System.Text;
using CrashScope.Agent.Runtime;

namespace CrashScope.Agent.Tests;

public sealed class ExistingCrashScopeInstanceProbeTests
{
    [Fact]
    public async Task IsRunningAsync_ReturnsTrueForHealthyCrashScopeMetadata()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("http://localhost:5077/api", request.RequestUri?.AbsoluteUri);
            return JsonResponse("{\"name\":\"CrashScope.Agent\",\"status\":\"running\"}");
        });
        using var client = new HttpClient(handler);
        var probe = new ExistingCrashScopeInstanceProbe(client);

        var running = await probe.IsRunningAsync(new Uri("http://localhost:5077/"));

        Assert.True(running);
    }

    [Fact]
    public async Task IsRunningAsync_ReturnsFalseForUnrelatedHttpService()
    {
        var handler = new StubHandler(_ =>
            JsonResponse("{\"name\":\"SomethingElse\",\"status\":\"running\"}"));
        using var client = new HttpClient(handler);
        var probe = new ExistingCrashScopeInstanceProbe(client);

        var running = await probe.IsRunningAsync(new Uri("http://localhost:5077/"));

        Assert.False(running);
    }

    [Fact]
    public async Task IsRunningAsync_ReturnsFalseWhenEndpointCannotBeReached()
    {
        var handler = new ThrowingHandler();
        using var client = new HttpClient(handler);
        var probe = new ExistingCrashScopeInstanceProbe(client);

        var running = await probe.IsRunningAsync(new Uri("http://localhost:5077/"));

        Assert.False(running);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }
}