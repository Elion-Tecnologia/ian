using System.Net;

namespace Ian.Tests;

public sealed class GeminiSmokeTransportTests
{
    [Fact]
    public async Task LocalScenariosCannotDispatchAnyRequest()
    {
        var inner = new FakeHandler();
        using var transport = new GeminiSmokeTransport(0, inner);
        using var client = new HttpClient(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PostAsync(Endpoint, null));
        Assert.Equal(0, inner.Calls);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(1, transport.Blocked);
    }

    [Fact]
    public async Task OneGenerationAllowedAndSecondIsBlockedWithoutDispatch()
    {
        var inner = new FakeHandler();
        using var transport = new GeminiSmokeTransport(1, inner);
        using var client = new HttpClient(transport);
        using var response = await client.PostAsync(Endpoint, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PostAsync(Endpoint, null));
        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(1, transport.Blocked);
    }

    [Theory]
    [InlineData("https://other.example/models/gemini-3.1-flash-lite:generateContent")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/files")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/models/other:generateContent")]
    public async Task OtherDestinationsAreBlocked(string endpoint)
    {
        var inner = new FakeHandler();
        using var transport = new GeminiSmokeTransport(1, inner);
        using var client = new HttpClient(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PostAsync(endpoint, null));
        Assert.Equal(0, inner.Calls);
        Assert.Equal(0, transport.Calls);
    }

    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent";

    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
