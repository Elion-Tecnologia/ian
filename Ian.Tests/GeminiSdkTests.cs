using System.Net;
using System.Text;
using System.Text.Json;
using Ian.Application.AI;
using Ian.Infrastructure.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class GeminiSdkTests
{
    [Theory]
    [InlineData(401, AiModelFailure.AuthenticationFailed)]
    [InlineData(403, AiModelFailure.AuthenticationFailed)]
    [InlineData(429, AiModelFailure.QuotaExceeded)]
    [InlineData(503, AiModelFailure.ProviderUnavailable)]
    [InlineData(404, AiModelFailure.ModelUnavailable)]
    public async Task RealSdkErrorsFlowThroughMafAndTheApplicationContract(int status, AiModelFailure expected)
    {
        var calls = 0;
        using var factory = Factory(new CallbackHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Response(status,
                JsonSerializer.Serialize(new { error = new { code = status, message = "PRIVATE_SYNTHETIC_BODY", status = "ERROR" } })));
        }));
        var result = await new GeminiAiModel(Settings(), factory.GetClient).InvokeAsync("synthetic prompt");

        Assert.Equal(expected, result.Failure);
        Assert.Equal(1, calls); // SDK retries are deliberately disabled.
        Assert.Null(result.Text);
        Assert.DoesNotContain("PRIVATE_SYNTHETIC_BODY", result.ErrorMessage);
    }

    [Fact]
    public async Task RealSdkResponseReturnsProjectedUsage()
    {
        using var factory = Factory(new CallbackHandler((_, _) => Task.FromResult(Response(200,
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"synthetic answer"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":3,"candidatesTokenCount":1,"totalTokenCount":4}}"""))));

        var result = await new GeminiAiModel(Settings(), factory.GetClient).InvokeAsync("synthetic prompt");

        Assert.True(result.IsSuccess);
        Assert.Equal("synthetic answer", result.Text);
        Assert.Equal(new AiModelUsage(3, 1, 4), result.Usage);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")]
    public async Task MalformedEmptyOrBlockedSdkResponseIsNotSuccess(string response)
    {
        using var factory = Factory(new CallbackHandler((_, _) => Task.FromResult(Response(200, response))));

        var result = await new GeminiAiModel(Settings(), factory.GetClient).InvokeAsync("synthetic prompt");

        Assert.Equal(AiModelFailure.InvalidResponse, result.Failure);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task FunctionCallingRoundTripUsesRealMafSdkAndLocalHttpOnly()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        var calls = 0;
        var resultReturned = false;
        app.MapPost("/{**path}", async context =>
        {
            Assert.Equal("synthetic-key", context.Request.Headers["x-goog-api-key"].ToString());
            Assert.Contains("gemini-3.8-flash", context.Request.Path.Value);
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                Assert.Contains("sum_demo", body.RootElement.GetProperty("tools").GetRawText());
            }
            else
            {
                var parts = body.RootElement.GetProperty("contents").EnumerateArray()
                    .SelectMany(content => content.GetProperty("parts").EnumerateArray());
                var functionResponse = Assert.Single(parts, part => part.TryGetProperty("functionResponse", out _))
                    .GetProperty("functionResponse");
                Assert.Equal("sum_demo", functionResponse.GetProperty("name").GetString());
                Assert.Contains("5", functionResponse.GetProperty("response").GetRawText());
                resultReturned = true;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(call == 1
                ? """{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"sum_demo","args":{"left":2,"right":3}}}]},"finishReason":"STOP"}]}"""
                : """{"candidates":[{"content":{"role":"model","parts":[{"text":"5"}]},"finishReason":"STOP"}]}""",
                context.RequestAborted);
        });
        await app.StartAsync();
        using var factory = Factory(new LoopbackHandler(new Uri(app.Urls.Single())));

        var demonstration = await GeminiFunctionDemo.RunAsync(factory.GetClient(), default);

        Assert.Equal(2, calls);
        Assert.True(demonstration.ArgumentsCorrect);
        Assert.Equal(1, demonstration.Invocations);
        Assert.True(resultReturned);
        Assert.False(string.IsNullOrWhiteSpace(demonstration.Text));
    }

    private static IOptions<GeminiOptions> Settings() => Options.Create(new GeminiOptions
    {
        ApiKey = "synthetic-key",
        Model = "gemini-3.8-flash",
        TimeoutSeconds = 5
    });

    private static GeminiChatClientFactory Factory(HttpMessageHandler handler) =>
        new(Settings(), new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });

    private static HttpResponseMessage Response(int status, string json) => new((HttpStatusCode)status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class LoopbackHandler(Uri address)
        : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.NotNull(request.RequestUri);
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri.Host);
            request.RequestUri = new Uri(address, request.RequestUri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
