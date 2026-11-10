using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Google.GenAI;
using Ian.Application.Telegram;
using Ian.Infrastructure.AI;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class GeminiVoiceTranscriberTests
{
    private const string Accepted = """{"status":"transcribed","text":"Consultar agenda","uncertainSegments":[]}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealTranscriberWithDecoratorPreservesIdentityOrBlocksUncertainty(bool uncertain)
    {
        var harness = new VoiceHarness();
        var fake = new FakeClient { Text = uncertain
            ? """{"status":"ambiguous","text":"","uncertainSegments":["nome"]}""" : Accepted };
        var handler = new TelegramVoiceUpdateHandler(harness, harness, Transcriber(fake), harness, new(), 42);
        var original = new TelegramInboxUpdate(987,
            """{"update_id":987,"message":{"message_id":17,"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"voice":{"file_id":"synthetic","duration":1}}}""");
        await handler.HandleAsync(original, default);
        Assert.All(harness.Audio, value => Assert.Equal(0, value));
        if (uncertain)
        {
            Assert.Empty(harness.Updates);
            Assert.Equal(TelegramVoiceFailure.NeedsClarification, Assert.Single(harness.Failures));
        }
        else
        {
            var forwarded = Assert.Single(harness.Updates);
            Assert.Equal(original.UpdateId, forwarded.UpdateId);
            using var json = JsonDocument.Parse(forwarded.PayloadJson);
            Assert.Equal(987, json.RootElement.GetProperty("update_id").GetInt64());
            Assert.Equal(17, json.RootElement.GetProperty("message").GetProperty("message_id").GetInt64());
            Assert.Equal(42, json.RootElement.GetProperty("message").GetProperty("from").GetProperty("id").GetInt64());
            Assert.Empty(harness.Failures);
        }
        Assert.Contains("voice", original.PayloadJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncationAndUnexpectedToolCallAreRejectedWithoutExecution(bool toolCall)
    {
        var fake = new FakeClient { Override = toolCall
            ? new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("id", "forbidden", new Dictionary<string, object?>())]))
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, Accepted)) { FinishReason = ChatFinishReason.Length } };
        using var file = new TelegramVoiceFile(SyntheticOggOpus.Create());
        Assert.Equal(TelegramVoiceFailure.NeedsClarification,
            (await Transcriber(fake).TranscribeAsync(file, 120, default)).Failure);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task ExactDurationBoundaryPassesAndPreCancelledCallDoesNotReadAudio()
    {
        using var file = new TelegramVoiceFile(SyntheticOggOpus.Create(frames: 50));
        var fake = new FakeClient();
        Assert.Null((await Transcriber(fake).TranscribeAsync(file, 1, default)).Failure);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        file.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Transcriber(fake).TranscribeAsync(file, 1, cancelled.Token));
        Assert.Equal(1, fake.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ValidOggOpusUsesMafWithBorrowedMemoryAndNoTools(int channels)
    {
        var bytes = SyntheticOggOpus.Create(channels: channels);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(38), 312); // Opus pre-skip in header.
        SyntheticOggOpus.SetCrc(bytes.AsSpan(0, 47));
        var fake = new FakeClient();
        var file = new TelegramVoiceFile(bytes);
        var result = await Transcriber(fake).TranscribeAsync(file, 120, default);
        Assert.Equal("Consultar agenda", result.Text);
        Assert.Null(result.Failure);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(ChatToolMode.None, fake.Options?.ToolMode);
        Assert.True(fake.Options?.Tools is null || fake.Options.Tools.Count == 0);
        Assert.NotNull(fake.Options?.ResponseFormat);
        var data = Assert.IsType<DataContent>(Assert.Single(fake.Messages.Single(m => m.Role == ChatRole.User).Contents));
        Assert.Equal("audio/ogg", data.MediaType);
        Assert.True(data.Data.Span.SequenceEqual(bytes));
        Assert.Contains(bytes, b => b != 0); // Transcriber borrows; owner disposes after use.
        file.Dispose();
        Assert.All(bytes, b => Assert.Equal(0, b));
        Assert.True(data.Data.Span.IndexOfAnyExcept((byte)0) < 0);
        Assert.Throws<ObjectDisposedException>(() => file.Content);
        Assert.DoesNotContain("Consultar", result.ToString());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("wav")]
    [InlineData("crc")]
    [InlineData("truncated")]
    [InlineData("no-eos")]
    [InlineData("sequence")]
    [InlineData("granule")]
    [InlineData("chain")]
    [InlineData("codec")]
    public async Task InvalidContainersNeverReachProvider(string kind)
    {
        var bytes = SyntheticOggOpus.Create(packetOverride: kind == "codec" ? [3, 0] : null);
        switch (kind)
        {
            case "empty": bytes = []; break;
            case "wav": bytes = "RIFF-synthetic"u8.ToArray(); break;
            case "crc": bytes[^1] ^= 1; break;
            case "truncated": bytes = bytes[..^1]; break;
            case "chain": bytes = [.. bytes, .. bytes]; break;
            case "sequence":
                bytes[18] = 2;
                SyntheticOggOpus.SetCrc(bytes.AsSpan(0, 47));
                break;
            case "no-eos":
            case "granule":
                var lastPage = LastPage(bytes);
                if (kind == "no-eos") bytes[lastPage + 5] = 0;
                else BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(lastPage + 6), 1);
                SyntheticOggOpus.SetCrc(bytes.AsSpan(lastPage));
                break;
        }
        var fake = new FakeClient();
        using var file = new TelegramVoiceFile(bytes);
        Assert.Equal(TelegramVoiceFailure.InvalidFile, (await Transcriber(fake).TranscribeAsync(file, 120, default)).Failure);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task DecodedDurationCannotBeHiddenByForgedGranule()
    {
        var bytes = SyntheticOggOpus.Create(frames: 65);
        var last = LastPage(bytes);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(last + 6), 48_000);
        SyntheticOggOpus.SetCrc(bytes.AsSpan(last));
        using var file = new TelegramVoiceFile(bytes);
        var fake = new FakeClient();
        Assert.Equal(TelegramVoiceFailure.DurationLimitExceeded,
            (await Transcriber(fake).TranscribeAsync(file, 1, default)).Failure);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task SizeLimitAndDecodedSilenceAvoidExternalCalls()
    {
        var fake = new FakeClient();
        using var file = new TelegramVoiceFile(SyntheticOggOpus.Create());
        Assert.Equal(TelegramVoiceFailure.SizeLimitExceeded,
            (await Transcriber(fake, maxBytes: 10).TranscribeAsync(file, 120, default)).Failure);
        using var silence = new TelegramVoiceFile(SyntheticOggOpus.Create(silent: true));
        Assert.Equal(TelegramVoiceFailure.EmptyTranscription,
            (await Transcriber(fake).TranscribeAsync(silence, 120, default)).Failure);
        var mutedBytes = SyntheticOggOpus.Create();
        BinaryPrimitives.WriteInt16LittleEndian(mutedBytes.AsSpan(44), short.MinValue);
        SyntheticOggOpus.SetCrc(mutedBytes.AsSpan(0, 47));
        using var muted = new TelegramVoiceFile(mutedBytes);
        Assert.Equal(TelegramVoiceFailure.EmptyTranscription,
            (await Transcriber(fake).TranscribeAsync(muted, 120, default)).Failure);
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData("", TelegramVoiceFailure.EmptyTranscription)]
    [InlineData("{", TelegramVoiceFailure.NeedsClarification)]
    [InlineData("{}", TelegramVoiceFailure.NeedsClarification)]
    [InlineData("{\"status\":\"transcribed\",\"text\":\"\",\"uncertainSegments\":[]}", TelegramVoiceFailure.EmptyTranscription)]
    [InlineData("{\"status\":\"empty\",\"text\":\"\",\"uncertainSegments\":[]}", TelegramVoiceFailure.EmptyTranscription)]
    [InlineData("{\"status\":\"unintelligible\",\"text\":\"\",\"uncertainSegments\":[]}", TelegramVoiceFailure.NeedsClarification)]
    [InlineData("{\"status\":\"ambiguous\",\"text\":\"talvez\",\"uncertainSegments\":[]}", TelegramVoiceFailure.NeedsClarification)]
    [InlineData("{\"status\":\"transcribed\",\"text\":\"Nome [inaudível]\",\"uncertainSegments\":[]}", TelegramVoiceFailure.NeedsClarification)]
    [InlineData("{\"status\":\"transcribed\",\"text\":\"Nome 42\",\"uncertainSegments\":[\"42\"]}", TelegramVoiceFailure.NeedsClarification)]
    [InlineData("{\"status\":\"empty\",\"text\":\"contradição\",\"uncertainSegments\":[]}", TelegramVoiceFailure.NeedsClarification)]
    public async Task EmptyInvalidOrUncertainResponsesNeverYieldCommand(string response, TelegramVoiceFailure expected)
    {
        using var file = new TelegramVoiceFile(SyntheticOggOpus.Create());
        var result = await Transcriber(new FakeClient { Text = response }).TranscribeAsync(file, 120, default);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.Text);
    }

    [Theory]
    [InlineData(429, TelegramVoiceFailure.RateLimited)]
    [InlineData(408, TelegramVoiceFailure.Timeout)]
    [InlineData(504, TelegramVoiceFailure.Timeout)]
    [InlineData(401, TelegramVoiceFailure.TranscriptionUnavailable)]
    [InlineData(404, TelegramVoiceFailure.TranscriptionUnavailable)]
    [InlineData(503, TelegramVoiceFailure.TranscriptionUnavailable)]
    public async Task ProviderFailureIsTypedAndPrivate(int status, TelegramVoiceFailure expected)
    {
        var fake = new FakeClient { Failure = new ApiException("PRIVATE_BODY", status, "ERROR", null) };
        var bytes = SyntheticOggOpus.Create();
        using (var file = new TelegramVoiceFile(bytes))
        {
            var result = await Transcriber(fake).TranscribeAsync(file, 120, default);
            Assert.Equal(expected, result.Failure);
            Assert.Null(result.Text);
            Assert.DoesNotContain("PRIVATE", result.ToString());
        }
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task NetworkTimeoutCancellationAndMissingConfigurationAreControlled()
    {
        using var file = new TelegramVoiceFile(SyntheticOggOpus.Create());
        Assert.Equal(TelegramVoiceFailure.TranscriptionUnavailable,
            (await Transcriber(new FakeClient { Failure = new HttpRequestException("PRIVATE") }).TranscribeAsync(file, 120, default)).Failure);
        Assert.Equal(TelegramVoiceFailure.Timeout,
            (await Transcriber(new FakeClient { Wait = true }, timeout: 1).TranscribeAsync(file, 120, default)).Failure);
        using var cancellation = new CancellationTokenSource();
        var fake = new FakeClient { Wait = true };
        var task = Transcriber(fake).TranscribeAsync(file, 120, cancellation.Token);
        await fake.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var unconfigured = new GeminiVoiceTranscriber(Options.Create(new GeminiOptions()),
            Options.Create(new TelegramVoiceOptions()), () => throw new InvalidOperationException());
        Assert.Equal(TelegramVoiceFailure.TranscriptionUnavailable,
            (await unconfigured.TranscribeAsync(file, 120, default)).Failure);
    }

    [Theory]
    [InlineData(200, null)]
    [InlineData(429, TelegramVoiceFailure.RateLimited)]
    public async Task InstalledSdkSerializesInlineAudioAndSchemaWithoutTools(int status, TelegramVoiceFailure? failure)
    {
        var calls = 0;
        var audio = SyntheticOggOpus.Create();
        using var factory = new GeminiChatClientFactory(Settings(), new HttpClient(new CallbackHandler(async request =>
        {
            calls++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.False(root.TryGetProperty("tools", out _));
            var parts = root.GetProperty("contents").EnumerateArray().SelectMany(c => c.GetProperty("parts").EnumerateArray());
            var inline = Assert.Single(parts, p => p.TryGetProperty("inlineData", out _)).GetProperty("inlineData");
            Assert.Equal("audio/ogg", inline.GetProperty("mimeType").GetString());
            Assert.Equal(audio, inline.GetProperty("data").GetBytesFromBase64());
            Assert.Equal("application/json", root.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
            return new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(status == 200
                    ? JsonSerializer.Serialize(new { candidates = new[] { new { content = new { role = "model", parts = new[] { new { text = Accepted } } }, finishReason = "STOP" } } })
                    : """{"error":{"code":429,"message":"PRIVATE","status":"RESOURCE_EXHAUSTED"}}""", Encoding.UTF8, "application/json")
            };
        })));
        using var file = new TelegramVoiceFile(audio);
        var result = await new GeminiVoiceTranscriber(Settings(), Options.Create(new TelegramVoiceOptions()), factory.GetClient)
            .TranscribeAsync(file, 120, default);
        Assert.Equal(failure, result.Failure);
        Assert.Equal(1, calls);
        if (failure is null) Assert.Equal("Consultar agenda", result.Text);
    }

    private static int LastPage(byte[] bytes)
    {
        var last = 0;
        for (var offset = 0; offset < bytes.Length;)
        {
            last = offset;
            var count = bytes[offset + 26];
            var body = 0;
            for (var i = 0; i < count; i++) body += bytes[offset + 27 + i];
            offset += 27 + count + body;
        }
        return last;
    }

    private static IOptions<GeminiOptions> Settings(int timeout = 60) => Options.Create(new GeminiOptions
        { ApiKey = "synthetic-key", Model = "gemini-3.1-flash-lite", TimeoutSeconds = timeout });

    private static GeminiVoiceTranscriber Transcriber(FakeClient fake, int timeout = 60, int maxBytes = 5242880) =>
        new(Settings(timeout), Options.Create(new TelegramVoiceOptions { MaxBytes = maxBytes }), () => fake);

    private sealed class CallbackHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }

    private sealed class FakeClient : IChatClient
    {
        public ChatResponse? Override { get; init; }
        public string Text { get; init; } = Accepted;
        public Exception? Failure { get; init; }
        public bool Wait { get; init; }
        public int Calls { get; private set; }
        public List<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Messages = messages.ToList();
            Options = options;
            Started.TrySetResult();
            if (Failure is not null) throw Failure;
            if (Wait) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Override ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, Text));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class VoiceHarness : ITelegramVoiceFileClient, ITelegramVoiceFeedback, ITelegramAuthorizedUpdateHandler
    {
        public byte[] Audio { get; } = SyntheticOggOpus.Create();
        public List<TelegramInboxUpdate> Updates { get; } = [];
        public List<TelegramVoiceFailure> Failures { get; } = [];
        public Task<TelegramVoiceDownloadResult> DownloadAsync(string fileId, int maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(TelegramVoiceDownloadResult.Downloaded(new TelegramVoiceFile(Audio)));
        public Task SendAsync(long chatId, TelegramVoiceFailure failure, CancellationToken cancellationToken)
        {
            Failures.Add(failure);
            return Task.CompletedTask;
        }
        public Task HandleAsync(TelegramInboxUpdate update, CancellationToken cancellationToken)
        {
            Updates.Add(update);
            return Task.CompletedTask;
        }
    }
}
