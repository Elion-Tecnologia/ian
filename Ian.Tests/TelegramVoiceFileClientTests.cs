using System.Net;
using System.Text;
using System.Text.Json;
using Ian.Application.Telegram;
using Ian.Infrastructure.Telegram;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class TelegramVoiceFileClientTests
{
    private const string Metadata = """{"ok":true,"result":{"file_path":"voice/test.ogg","file_size":4}}""";

    [Fact]
    public async Task UsesGetFileAndDisposesDownloadStream()
    {
        var stream = new ObservedStream([1, 2, 3, 4]);
        var requests = 0;
        using var client = CreateClient(new ScriptedHandler(async (request, cancellation) =>
        {
            requests++;
            if (requests == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.EndsWith("/botTEST/getFile", request.RequestUri!.AbsolutePath);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
                Assert.Equal("synthetic", body.RootElement.GetProperty("file_id").GetString());
                return Json(Metadata);
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.telegram.org/file/botTEST/voice/test.ogg", request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }));

        var result = await client.DownloadAsync("synthetic", 8, default);

        using var file = result.File;
        Assert.NotNull(file);
        Assert.Null(result.Failure);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, file.Content.ToArray());
        Assert.True(stream.Disposed);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("[]")]
    [InlineData("{\"ok\":false}")]
    [InlineData("{\"ok\":true,\"result\":{}}")]
    [InlineData("{\"ok\":true,\"result\":{\"file_path\":\"../secret\"}}")]
    [InlineData("{\"ok\":true,\"result\":{\"file_path\":\"https://other.example/file\"}}")]
    [InlineData("{\"ok\":true,\"result\":{\"file_path\":\"voice/%2e%2e/file\"}}")]
    public async Task InvalidMetadataNeverStartsDownload(string metadata)
    {
        var calls = 0;
        using var client = CreateClient(new ScriptedHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(metadata));
        }));

        var result = await client.DownloadAsync("synthetic", 8, default);

        Assert.Equal(TelegramVoiceFailure.InvalidFile, result.Failure);
        Assert.Null(result.File);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(400, TelegramVoiceFailure.InvalidFile)]
    [InlineData(404, TelegramVoiceFailure.InvalidFile)]
    [InlineData(429, TelegramVoiceFailure.RateLimited)]
    [InlineData(500, TelegramVoiceFailure.DownloadUnavailable)]
    [InlineData(302, TelegramVoiceFailure.DownloadUnavailable)]
    public async Task HttpFailureHasTypedResult(int status, TelegramVoiceFailure expected)
    {
        using var client = CreateClient(new ScriptedHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));

        var result = await client.DownloadAsync("synthetic", 8, default);

        Assert.Null(result.File);
        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task GetFileSizeIsCheckedBeforeDownload()
    {
        var calls = 0;
        using var client = CreateClient(new ScriptedHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(Metadata.Replace("\"file_size\":4", "\"file_size\":9")));
        }));

        var result = await client.DownloadAsync("synthetic", 8, default);

        Assert.Equal(TelegramVoiceFailure.SizeLimitExceeded, result.Failure);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ActualBytesEnforceLimitWithoutContentLengthAndDisposeStream()
    {
        var stream = new ObservedStream(new byte[9]);
        var calls = 0;
        using var client = CreateClient(new ScriptedHandler((_, _) =>
            Task.FromResult(++calls == 1 ? Json(Metadata) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));

        var result = await client.DownloadAsync("synthetic", 8, default);

        Assert.Equal(TelegramVoiceFailure.SizeLimitExceeded, result.Failure);
        Assert.Null(result.File);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task InterruptedBodyIsControlledAndStreamIsDisposed()
    {
        var stream = new ObservedStream([1, 2, 3, 4]) { FailRead = true };
        var calls = 0;
        using var client = CreateClient(new ScriptedHandler((_, _) =>
            Task.FromResult(++calls == 1 ? Json(Metadata) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));

        var result = await client.DownloadAsync("synthetic", 8, default);

        Assert.Equal(TelegramVoiceFailure.DownloadUnavailable, result.Failure);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task NetworkErrorDoesNotEscapeWithTokenUrl()
    {
        using var client = CreateClient(new ScriptedHandler((_, _) =>
            throw new HttpRequestException("https://api.telegram.org/botTEST/getFile")));

        var result = await client.DownloadAsync("synthetic", 8, default);

        Assert.Equal(TelegramVoiceFailure.DownloadUnavailable, result.Failure);
        Assert.DoesNotContain("botTEST", result.ToString());
    }

    [Fact]
    public async Task FailedFeedbackDoesNotReportSuccessOrExposeOriginalException()
    {
        using var client = CreateClient(new ScriptedHandler((_, _) =>
            throw new HttpRequestException("https://api.telegram.org/botTEST/sendMessage")));

        var exception = await Assert.ThrowsAsync<TelegramVoiceFeedbackException>(() =>
            client.SendAsync(42, TelegramVoiceFailure.NeedsClarification, default));

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("botTEST", exception.ToString());
    }

    [Fact]
    public async Task FeedbackRequestsClarificationWithoutEchoingPrivateAudio()
    {
        using var client = CreateClient(new ScriptedHandler(async (request, cancellation) =>
        {
            Assert.EndsWith("/sendMessage", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            Assert.Equal(42, body.RootElement.GetProperty("chat_id").GetInt64());
            Assert.Contains("esclarecer", body.RootElement.GetProperty("text").GetString());
            return Json("""{"ok":true,"result":{"message_id":1}}""");
        }));

        await client.SendAsync(42, TelegramVoiceFailure.NeedsClarification, default);
    }

    [Theory]
    [InlineData(4, null)]
    [InlineData(8, null)]
    [InlineData(9, TelegramVoiceFailure.SizeLimitExceeded)]
    [InlineData(0, TelegramVoiceFailure.InvalidFile)]
    public async Task RealHttpStreamsRespectActualLimit(int bytes, TelegramVoiceFailure? expected)
    {
        await using var app = CreateServer();
        var getFileCalls = 0;
        var downloadCalls = 0;
        app.MapPost("/botTEST/getFile", () =>
        {
            Interlocked.Increment(ref getFileCalls);
            return Results.Json(new { ok = true, result = new { file_path = "voice/test.ogg" } });
        });
        app.MapGet("/file/botTEST/voice/test.ogg", async context =>
        {
            Interlocked.Increment(ref downloadCalls);
            await context.Response.Body.WriteAsync(new byte[bytes], context.RequestAborted);
        });
        await app.StartAsync();
        using var client = CreateClient(new LoopbackHandler(new Uri(app.Urls.Single())));

        var result = await client.DownloadAsync("synthetic", 8, default);

        using var file = result.File;
        Assert.Equal(expected, result.Failure);
        Assert.Equal(1, getFileCalls);
        Assert.Equal(1, downloadCalls);
        if (expected is null)
        {
            Assert.NotNull(file);
            Assert.Equal(bytes, file.Content.Length);
        }
        else
        {
            Assert.Null(file);
        }
    }

    [Fact]
    public async Task RealHttpBodyTimeoutReturnsControlledFailure()
    {
        await using var app = CreateServer();
        app.MapPost("/botTEST/getFile", () => Results.Json(new
        {
            ok = true,
            result = new { file_path = "voice/test.ogg" }
        }));
        app.MapGet("/file/botTEST/voice/test.ogg", async context =>
        {
            await context.Response.StartAsync(context.RequestAborted);
            await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
        });
        await app.StartAsync();
        using var client = CreateClient(new LoopbackHandler(new Uri(app.Urls.Single())), timeoutSeconds: 1);

        var result = await client.DownloadAsync("synthetic", 8, default);

        Assert.Equal(TelegramVoiceFailure.Timeout, result.Failure);
        Assert.Null(result.File);
    }

    private static WebApplication CreateServer()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        return app;
    }

    private static TelegramVoiceFileClient CreateClient(HttpMessageHandler handler, int timeoutSeconds = 60) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new TelegramOptions { BotToken = "TEST" }),
            Options.Create(new TelegramVoiceOptions { TimeoutSeconds = timeoutSeconds }));

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class ScriptedHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    // Only in tests: redirect the fixed Telegram URI to a local HTTP stand-in.
    private sealed class LoopbackHandler(Uri address)
        : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.NotNull(request.RequestUri);
            Assert.Equal("api.telegram.org", request.RequestUri.Host);
            request.RequestUri = new Uri(address, request.RequestUri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class ObservedStream(byte[] content) : MemoryStream(content)
    {
        public bool Disposed { get; private set; }
        public bool FailRead { get; init; }
        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            FailRead ? throw new IOException("Synthetic read failure") : base.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
