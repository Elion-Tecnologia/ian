using System.Net;
using System.Text;
using System.Text.Json;
using Ian.Application.Telegram;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class TelegramTextClientTests
{
    private const long Admin = 5_000_000_000;
    private const string Token = "123456:synthetic-private-token";
    private const string Text = "Resposta sintética com telefone 11999990000 e endereço privado.";
    private const string PrivateBody = "PRIVATE_TELEGRAM_BODY";

    [Fact]
    public async Task SendsPlainTextOnlyToTheAuthorizedPrivateChatAndReportsApiAcceptance()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Accepted()));
        var result = await fixture.Client.SendAsync(Source(), Text);

        Assert.True(result.IsAccepted);
        Assert.Equal(TelegramTextSendStatus.Accepted, result.Status);
        Assert.Equal(123, result.MessageId);
        Assert.Equal(1, result.Attempts);
        Assert.False(result.DeliveryUncertain);
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", request.Uri);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(Admin, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal(Text, body.RootElement.GetProperty("text").GetString());
        Assert.False(body.RootElement.TryGetProperty("parse_mode", out _));
        Assert.True(body.RootElement.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
        AssertSafe(fixture, result);
    }

    [Theory]
    [InlineData(500, TelegramTextSendStatus.ServerFailure, true)]
    [InlineData(503, TelegramTextSendStatus.ServerFailure, true)]
    [InlineData(504, TelegramTextSendStatus.ServerFailure, true)]
    [InlineData(401, TelegramTextSendStatus.AuthenticationFailure, false)]
    [InlineData(403, TelegramTextSendStatus.Rejected, false)]
    [InlineData(400, TelegramTextSendStatus.Rejected, false)]
    [InlineData(302, TelegramTextSendStatus.Rejected, false)]
    [InlineData(408, TelegramTextSendStatus.Timeout, true)]
    public async Task ClassifiesHttpFailuresWithoutBlindRetriesOrSensitiveDiagnostics(
        int status, TelegramTextSendStatus expected, bool uncertain)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(status, PrivateBody)));
        var result = await fixture.Client.SendAsync(Source(), Text);

        Assert.Equal(expected, result.Status);
        Assert.False(result.IsAccepted);
        Assert.Equal(uncertain, result.DeliveryUncertain);
        Assert.Null(result.MessageId);
        Assert.Equal(1, result.Attempts);
        Assert.Single(fixture.Handler.Requests);
        AssertSafe(fixture, result);
    }

    [Fact]
    public async Task RetriesOnlyAfterAnExplicitRateLimitRejectionAndItsRequestedDelay()
    {
        var responses = new Queue<HttpResponseMessage>([RateLimited(1), Accepted()]);
        using var fixture = new Fixture((_, _) => Task.FromResult(responses.Dequeue()));
        var started = TimeProvider.System.GetTimestamp();
        var result = await fixture.Client.SendAsync(Source(), Text);

        Assert.True(result.IsAccepted);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.True(TimeProvider.System.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(950));
        Assert.Equal(fixture.Handler.Requests[0].Body, fixture.Handler.Requests[1].Body);
        AssertSafe(fixture, result);
    }

    [Fact]
    public async Task RateLimitRetriesStopAtTheConfiguredAttemptLimit()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(RateLimited(1)), new() { MaxAttempts = 2 });
        var result = await fixture.Client.SendAsync(Source(), Text);

        Assert.Equal(TelegramTextSendStatus.RateLimited, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(1), result.RetryAfter);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.False(result.DeliveryUncertain);
    }

    [Theory]
    [InlineData("{\"ok\":false,\"error_code\":429}", null)]
    [InlineData("{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":0}}", null)]
    [InlineData("{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":-1}}", null)]
    [InlineData("{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":\"1\"}}", null)]
    [InlineData("{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":2147483648}}", null)]
    [InlineData("{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":60}}", 60)]
    [InlineData("invalid json", null)]
    public async Task MissingInvalidOrExcessiveRetryAfterNeverCreatesAnImmediateOrLongRetry(string body, int? seconds)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(429, body)));
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.RateLimited, result.Status);
        Assert.Equal(seconds is { } value ? TimeSpan.FromSeconds(value) : null, result.RetryAfter);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task RetryWaitCannotExceedTheTotalSendBudget()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(RateLimited(1)), new()
        { RequestTimeoutSeconds = 1, TotalTimeoutSeconds = 1 });
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.RateLimited, result.Status);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task PerRequestTimeoutIsUncertainAndIsNeverRetried()
    {
        using var fixture = new Fixture(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Accepted();
        }, new() { RequestTimeoutSeconds = 1 });
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.Timeout, result.Status);
        Assert.True(result.DeliveryUncertain);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task TotalTimeoutIncludesRateLimitWaitAndTheFollowingAttempt()
    {
        var attempts = 0;
        using var fixture = new Fixture(async (_, token) =>
        {
            if (++attempts == 1) return RateLimited(1);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Accepted();
        }, new() { RequestTimeoutSeconds = 2, TotalTimeoutSeconds = 2 });
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.Timeout, result.Status);
        Assert.True(result.DeliveryUncertain);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task TimeoutAlsoCoversResponseBodyRead()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new WaitingStream()) }), new() { RequestTimeoutSeconds = 1 });
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.Timeout, result.Status);
        Assert.True(result.DeliveryUncertain);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotSend()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Accepted()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Client.SendAsync(Source(), Text, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task CancellationDuringSendPropagatesWithoutProviderExceptionDetailsOrRetries()
    {
        using var fixture = new Fixture(async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { throw new HttpRequestException($"{Token} {Text} {PrivateBody}"); }
            return Accepted();
        });
        using var cancellation = new CancellationTokenSource();
        var send = fixture.Client.SendAsync(Source(), Text, cancellation.Token);
        await fixture.Handler.Started.Task;
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(Token, exception.ToString());
        Assert.DoesNotContain(Text, exception.ToString());
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task CancellationDuringRateLimitWaitPreventsAnotherAttempt()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(RateLimited(1)));
        using var cancellation = new CancellationTokenSource();
        var send = fixture.Client.SendAsync(Source(), Text, cancellation.Token);
        await fixture.Logger.Logged.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportOrUnexpectedFailureIsUncertainSanitizedAndNeverRetried(bool unexpected)
    {
        using var fixture = new Fixture((_, _) =>
        {
            if (unexpected) throw new InvalidOperationException($"{Token} {Text} {PrivateBody}");
            throw new HttpRequestException($"{Token} {Text} {PrivateBody}");
        });
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(unexpected ? TelegramTextSendStatus.UnexpectedFailure : TelegramTextSendStatus.TransportFailure, result.Status);
        Assert.True(result.DeliveryUncertain);
        Assert.Single(fixture.Handler.Requests);
        AssertSafe(fixture, result);
    }

    [Theory]
    [InlineData("{\"ok\":true}")]
    [InlineData("{\"ok\":true,\"result\":{\"message_id\":\"123\"}}")]
    [InlineData("{\"ok\":true,\"result\":{\"message_id\":123,\"chat\":{\"id\":99,\"type\":\"private\"}}}")]
    [InlineData("{\"ok\":true,\"result\":{\"message_id\":123,\"chat\":{\"id\":5000000000,\"type\":\"group\"}}}")]
    [InlineData("{\"ok\":false,\"error_code\":\"401\"}")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task InvalidOrMismatchedSuccessResponsesAreUncertainAndNeverRetried(string body)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(200, body)));
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.InvalidResponse, result.Status);
        Assert.False(result.IsAccepted);
        Assert.True(result.DeliveryUncertain);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task OversizedProviderResponseIsBoundedAndNotClaimedAsAccepted()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(200, new string('x', 65_537))));
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.InvalidResponse, result.Status);
        Assert.True(result.DeliveryUncertain);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task ApiLevelErrorInsideHttpSuccessIsNotAnAcceptance()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(200, "{\"ok\":false,\"error_code\":401}")));
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.AuthenticationFailure, result.Status);
        Assert.False(result.IsAccepted);
        Assert.Single(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(99, 99, "private", false)]
    [InlineData(99, 5000000000, "private", false)]
    [InlineData(5000000000, 99, "private", false)]
    [InlineData(5000000000, -100, "group", false)]
    [InlineData(5000000000, 5000000000, "supergroup", false)]
    [InlineData(5000000000, 5000000000, "channel", false)]
    [InlineData(5000000000, 5000000000, "private", true)]
    public async Task UnauthorizedRecipientsBotsAndGroupsNeverReachHttp(long user, long chat, string type, bool bot)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Accepted()));
        var result = await fixture.Client.SendAsync(Source(user, chat, type, bot), Text);
        Assert.Equal(TelegramTextSendStatus.UnauthorizedRecipient, result.Status);
        Assert.Equal(0, result.Attempts);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData("{}")] [InlineData("not json")] [InlineData("null")]
    [InlineData("{\"message\":{\"from\":{\"id\":5000000000,\"is_bot\":false},\"chat\":{\"id\":5000000000,\"type\":\"private\"},\"voice\":{}}}")]
    public async Task InvalidOrNonTextSourcesNeverReachHttp(string payload)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Accepted()));
        var result = await fixture.Client.SendAsync(new TelegramInboxUpdate(1, payload), Text);
        Assert.Equal(TelegramTextSendStatus.UnauthorizedRecipient, result.Status);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("invalid\0text")]
    public async Task InvalidTextDoesNotSend(string? text)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Accepted()));
        var result = await fixture.Client.SendAsync(Source(), text!);
        Assert.Equal(TelegramTextSendStatus.InvalidMessage, result.Status);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(4096, TelegramTextSendStatus.Accepted, 1)]
    [InlineData(4097, TelegramTextSendStatus.InvalidMessage, 0)]
    public async Task TextLimitDoesNotTruncateOrSplitMessages(int length, TelegramTextSendStatus expected, int requests)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Accepted()));
        var result = await fixture.Client.SendAsync(Source(), new string('x', length));
        Assert.Equal(expected, result.Status);
        Assert.Equal(requests, fixture.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("")] [InlineData("secret/path")] [InlineData("secret?query")] [InlineData("secret#fragment")]
    public async Task UnsafeTokenCannotChangeTheTrustedApiEndpoint(string token)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Accepted()), token: token);
        var result = await fixture.Client.SendAsync(Source(), Text);
        Assert.Equal(TelegramTextSendStatus.InvalidConfiguration, result.Status);
        Assert.Empty(fixture.Handler.Requests);
    }

    private static TelegramInboxUpdate Source(long user = Admin, long chat = Admin, string type = "private", bool bot = false) =>
        new(1234, JsonSerializer.Serialize(new
        {
            update_id = 1234,
            message = new { from = new { id = user, is_bot = bot }, chat = new { id = chat, type }, text = "Solicitação sintética" }
        }));

    private static HttpResponseMessage Accepted() => Response(200, JsonSerializer.Serialize(new
    { ok = true, result = new { message_id = 123, chat = new { id = Admin, type = "private" } } }));
    private static HttpResponseMessage RateLimited(int seconds) => Response(429, JsonSerializer.Serialize(new
    { ok = false, error_code = 429, description = PrivateBody, parameters = new { retry_after = seconds } }));
    private static HttpResponseMessage Response(int status, string body) => new((HttpStatusCode)status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static void AssertSafe(Fixture fixture, TelegramTextSendResult result)
    {
        var diagnostics = string.Join('\n', fixture.Logger.Messages) + result;
        foreach (var sensitive in new[] { Token, Text, PrivateBody, "11999990000", Admin.ToString() })
            Assert.DoesNotContain(sensitive, diagnostics);
    }

    private sealed class Fixture : IDisposable
    {
        public FakeHandler Handler { get; }
        public CapturingLogger Logger { get; } = new();
        public TelegramTextClient Client { get; }
        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
            TelegramTextSendingOptions? limits = null, string token = Token)
        {
            Handler = new FakeHandler(send);
            Client = new(new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan },
                Options.Create(new TelegramOptions { BotToken = token, AdministratorUserId = Admin }),
                Options.Create(limits ?? new()), TimeProvider.System, Logger);
        }
        public void Dispose() => Client.Dispose();
    }

    private sealed record RequestSnapshot(HttpMethod Method, string Uri, string Body);
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(new(request.Method, request.RequestUri!.ToString(), await request.Content!.ReadAsStringAsync(token)));
            Started.TrySetResult();
            return await send(request, token);
        }
    }

    private sealed class CapturingLogger : ILogger<TelegramTextClient>
    {
        public List<string> Messages { get; } = [];
        public TaskCompletionSource Logged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Messages.Add(formatter(state, exception)); Logged.TrySetResult(); }
    }

    private sealed class WaitingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, token); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
