using System.Text.Json;
using System.Text.Json.Nodes;
using Ian.Application.Telegram;

namespace Ian.Tests;

public sealed class TelegramVoiceInputTests
{
    [Fact]
    public async Task AuthorizedVoiceBecomesTextWithOriginalIdentityAndClearsAudio()
    {
        var fixture = new Fixture();
        var original = Update();

        await fixture.DispatchAsync(original);

        var forwarded = Assert.Single(fixture.TextUpdates);
        using var document = JsonDocument.Parse(forwarded.PayloadJson);
        var message = document.RootElement.GetProperty("message");
        Assert.Equal(original.UpdateId, forwarded.UpdateId);
        Assert.Equal(42, message.GetProperty("from").GetProperty("id").GetInt64());
        Assert.Equal(42, message.GetProperty("chat").GetProperty("id").GetInt64());
        Assert.Equal(17, message.GetProperty("message_id").GetInt64());
        Assert.Equal("Consultar agenda", message.GetProperty("text").GetString());
        Assert.False(message.TryGetProperty("voice", out _));
        Assert.Contains("voice", original.PayloadJson);
        Assert.All(fixture.Audio, value => Assert.Equal(0, value));
        Assert.Empty(fixture.Failures);
        Assert.Equal(120, fixture.TranscriberDurationLimit);
    }

    [Theory]
    [InlineData(99, false, "private")]
    [InlineData(42, true, "private")]
    [InlineData(42, false, "group")]
    [InlineData(42, false, "supergroup")]
    public async Task UnauthorizedVoiceNeverDownloadsTranscribesOrReplies(long userId, bool isBot, string chatType)
    {
        var fixture = new Fixture();
        var update = Modify(message =>
        {
            message["from"] = new JsonObject { ["id"] = userId, ["is_bot"] = isBot };
            message["chat"] = new JsonObject { ["id"] = 42, ["type"] = chatType };
        });

        await fixture.DispatchAsync(update);
        // The decorator also protects direct invocation outside the dispatcher.
        await fixture.Handler.HandleAsync(update, default);

        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.Transcriptions);
        Assert.Empty(fixture.TextUpdates);
        Assert.Empty(fixture.Failures);
    }

    [Theory]
    [InlineData("duration", "121", TelegramVoiceFailure.DurationLimitExceeded)]
    [InlineData("duration", "0", TelegramVoiceFailure.InvalidFile)]
    [InlineData("duration", "\"long\"", TelegramVoiceFailure.InvalidFile)]
    [InlineData("file_size", "5242881", TelegramVoiceFailure.SizeLimitExceeded)]
    [InlineData("file_size", "-1", TelegramVoiceFailure.InvalidFile)]
    [InlineData("file_size", "\"large\"", TelegramVoiceFailure.InvalidFile)]
    public async Task InvalidMetadataIsRejectedBeforeDownload(string field, string json, TelegramVoiceFailure expected)
    {
        var fixture = new Fixture();
        var update = Modify(message => message["voice"]![field] = JsonNode.Parse(json));

        await fixture.DispatchAsync(update);

        Assert.Equal(expected, Assert.Single(fixture.Failures));
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.Transcriptions);
        Assert.Empty(fixture.TextUpdates);
    }

    [Fact]
    public async Task MissingOptionalSizeAndMimeAreAccepted()
    {
        var fixture = new Fixture();
        var update = Modify(message => message["voice"]!.AsObject().Remove("file_size"));

        await fixture.DispatchAsync(update);

        Assert.Single(fixture.TextUpdates);
        Assert.All(fixture.Audio, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(TelegramVoiceFailure.InvalidFile)]
    [InlineData(TelegramVoiceFailure.EmptyTranscription)]
    [InlineData(TelegramVoiceFailure.NeedsClarification)]
    [InlineData(TelegramVoiceFailure.RateLimited)]
    [InlineData(TelegramVoiceFailure.TranscriptionUnavailable)]
    public async Task RejectedTranscriptionNeverReachesTextHandlerAndClearsAudio(TelegramVoiceFailure failure)
    {
        var fixture = new Fixture
        {
            Transcription = VoiceTranscriptionResult.Rejected(failure)
        };

        await fixture.DispatchAsync(Update());

        Assert.Empty(fixture.TextUpdates);
        Assert.Equal(failure, Assert.Single(fixture.Failures));
        Assert.All(fixture.Audio, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task EmptyTranscriptionRequiresClarification()
    {
        var fixture = new Fixture
        {
            Transcription = VoiceTranscriptionResult.Transcribed("   ")
        };

        await fixture.DispatchAsync(Update());

        Assert.Equal(TelegramVoiceFailure.EmptyTranscription, Assert.Single(fixture.Failures));
        Assert.Empty(fixture.TextUpdates);
    }

    [Theory]
    [InlineData(TelegramVoiceFailure.SizeLimitExceeded)]
    [InlineData(TelegramVoiceFailure.InvalidFile)]
    [InlineData(TelegramVoiceFailure.DownloadUnavailable)]
    [InlineData(TelegramVoiceFailure.Timeout)]
    public async Task DownloadFailureNeverTranscribesOrExecutesText(TelegramVoiceFailure failure)
    {
        var fixture = new Fixture { DownloadFailure = failure };

        await fixture.DispatchAsync(Update());

        Assert.Equal(failure, Assert.Single(fixture.Failures));
        Assert.Equal(0, fixture.Transcriptions);
        Assert.Empty(fixture.TextUpdates);
    }

    [Fact]
    public async Task TimeoutClearsAudioAndSendsControlledFeedback()
    {
        var fixture = new Fixture { WaitForCancellation = true };
        fixture.Limits = new TelegramVoiceLimits(TimeoutSeconds: 1);

        await fixture.DispatchAsync(Update());

        Assert.Equal(TelegramVoiceFailure.Timeout, Assert.Single(fixture.Failures));
        Assert.Empty(fixture.TextUpdates);
        Assert.All(fixture.Audio, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task ShutdownOrLostLeaseCancelsWithoutSendingFeedbackAndClearsAudio()
    {
        var fixture = new Fixture { WaitForCancellation = true };
        using var cancellation = new CancellationTokenSource();
        var processing = fixture.DispatchAsync(Update(), cancellation.Token);
        await fixture.TranscriptionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Empty(fixture.TextUpdates);
        Assert.Empty(fixture.Failures);
        Assert.All(fixture.Audio, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task UnexpectedTranscriberFailureStillClearsAudio()
    {
        var fixture = new Fixture { ThrowInTranscriber = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.DispatchAsync(Update()));

        Assert.Empty(fixture.TextUpdates);
        Assert.All(fixture.Audio, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task PlainTextContinuesThroughExistingHandler()
    {
        var fixture = new Fixture();
        var update = Modify(message =>
        {
            message.Remove("voice");
            message["text"] = "Consultar agenda";
        });

        await fixture.DispatchAsync(update);

        Assert.Equal(update, Assert.Single(fixture.TextUpdates));
        Assert.Equal(0, fixture.Downloads);
    }

    [Fact]
    public void TranscriptionDiagnosticTextDoesNotExposePrivateContent()
    {
        var result = VoiceTranscriptionResult.Transcribed("private synthetic transcription");

        Assert.Equal("Transcribed", result.ToString());
        Assert.DoesNotContain("private synthetic transcription", result.ToString());
    }

    [Fact]
    public void ParserPreservesSizeWithoutAddingOtherVoiceFields()
    {
        using var document = JsonDocument.Parse(Update().PayloadJson);

        Assert.True(TelegramUpdateParser.TryParse(document.RootElement, out var update));
        Assert.NotNull(update);
        using var stored = JsonDocument.Parse(update.PayloadJson);
        var voice = stored.RootElement.GetProperty("message").GetProperty("voice");
        Assert.Equal(16, voice.GetProperty("file_size").GetInt64());
        Assert.False(voice.TryGetProperty("file_unique_id", out _));
    }

    private static TelegramInboxUpdate Update() => new(100,
        """{"update_id":100,"message":{"message_id":17,"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"voice":{"file_id":"synthetic-voice","file_size":16,"duration":12,"file_unique_id":"unused"}}}""");

    private static TelegramInboxUpdate Modify(Action<JsonObject> modify)
    {
        var root = JsonNode.Parse(Update().PayloadJson)!.AsObject();
        modify(root["message"]!.AsObject());
        return new TelegramInboxUpdate(100, root.ToJsonString());
    }

    private sealed class Fixture : ITelegramVoiceFileClient, IVoiceTranscriber,
        ITelegramVoiceFeedback, ITelegramAuthorizedUpdateHandler, ITelegramGroupEventHandler
    {
        public byte[] Audio { get; } = [1, 2, 3, 4];
        public List<TelegramInboxUpdate> TextUpdates { get; } = [];
        public List<TelegramVoiceFailure> Failures { get; } = [];
        public int Downloads { get; private set; }
        public int Transcriptions { get; private set; }
        public int TranscriberDurationLimit { get; private set; }
        public TelegramVoiceFailure? DownloadFailure { get; init; }
        public VoiceTranscriptionResult Transcription { get; init; } =
            VoiceTranscriptionResult.Transcribed("Consultar agenda");
        public bool WaitForCancellation { get; init; }
        public bool ThrowInTranscriber { get; init; }
        public TelegramVoiceLimits Limits { get; set; } = new();
        public TaskCompletionSource TranscriptionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TelegramVoiceUpdateHandler Handler => new(this, this, this, this, Limits, 42);

        public Task<TelegramAuthorizationDecision> DispatchAsync(
            TelegramInboxUpdate update, CancellationToken cancellationToken = default) =>
            new TelegramUpdateDispatcher(Handler, this).DispatchAsync(update, 42, cancellationToken);

        public Task<TelegramVoiceDownloadResult> DownloadAsync(
            string fileId, int maxBytes, CancellationToken cancellationToken)
        {
            Downloads++;
            return Task.FromResult(DownloadFailure is { } failure
                ? TelegramVoiceDownloadResult.Rejected(failure)
                : TelegramVoiceDownloadResult.Downloaded(new TelegramVoiceFile(Audio)));
        }

        public async Task<VoiceTranscriptionResult> TranscribeAsync(
            TelegramVoiceFile file, int maxDurationSeconds, CancellationToken cancellationToken)
        {
            Transcriptions++;
            TranscriberDurationLimit = maxDurationSeconds;
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, file.Content.ToArray());
            TranscriptionStarted.TrySetResult();
            if (ThrowInTranscriber)
            {
                throw new InvalidOperationException("Synthetic failure");
            }

            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Transcription;
        }

        public Task SendAsync(long chatId, TelegramVoiceFailure failure, CancellationToken cancellationToken)
        {
            Assert.Equal(42, chatId);
            Failures.Add(failure);
            return Task.CompletedTask;
        }

        public Task HandleAsync(TelegramInboxUpdate update, CancellationToken cancellationToken)
        {
            TextUpdates.Add(update);
            return Task.CompletedTask;
        }

        public Task HandleCandidateAsync(TelegramGroupEventCandidate candidate, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
