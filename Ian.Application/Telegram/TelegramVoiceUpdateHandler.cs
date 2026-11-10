using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ian.Application.Telegram;

// Kept outside the live dispatcher until the authorized text pipeline and real
// audio validation are available. The concrete decorator may be resolved in tests.
public sealed class TelegramVoiceUpdateHandler(
    ITelegramAuthorizedUpdateHandler textHandler,
    ITelegramVoiceFileClient fileClient,
    IVoiceTranscriber transcriber,
    ITelegramVoiceFeedback feedback,
    TelegramVoiceLimits limits,
    long administratorUserId) : ITelegramAuthorizedUpdateHandler
{
    public async Task HandleAsync(
        TelegramInboxUpdate update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!limits.IsValid())
        {
            throw new InvalidOperationException("Limites de voz inválidos.");
        }

        using var document = JsonDocument.Parse(update.PayloadJson);
        var root = document.RootElement;

        if (TelegramUpdateAuthorization.Evaluate(root, administratorUserId) !=
            TelegramAuthorizationDecision.Authorized)
        {
            return;
        }

        var message = root.GetProperty("message");
        if (!message.TryGetProperty("voice", out var voice))
        {
            await textHandler.HandleAsync(update, cancellationToken);
            return;
        }

        if (!message.GetProperty("chat").TryGetProperty("id", out var chatIdElement) ||
            chatIdElement.ValueKind != JsonValueKind.Number ||
            !chatIdElement.TryGetInt64(out var chatId) || chatId <= 0)
        {
            return;
        }

        var validationFailure = ValidateVoice(voice);
        if (validationFailure is not null)
        {
            await feedback.SendAsync(chatId, validationFailure.Value, cancellationToken);
            return;
        }

        VoiceTranscriptionResult transcription;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.TimeoutSeconds));
            try
            {
                var download = await fileClient.DownloadAsync(
                    voice.GetProperty("file_id").GetString() ?? string.Empty,
                    limits.MaxBytes,
                    timeout.Token);

                if (download.File is null)
                {
                    transcription = VoiceTranscriptionResult.Rejected(
                        download.Failure ?? TelegramVoiceFailure.DownloadUnavailable);
                }
                else
                {
                    using var file = download.File;
                    timeout.Token.ThrowIfCancellationRequested();
                    transcription = await transcriber.TranscribeAsync(
                        file, limits.MaxDurationSeconds, timeout.Token);
                    timeout.Token.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                transcription = VoiceTranscriptionResult.Rejected(TelegramVoiceFailure.Timeout);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (transcription.Failure is not null)
        {
            await feedback.SendAsync(chatId, transcription.Failure.Value, cancellationToken);
            return;
        }

        // Only the transient input changes. The original inbox payload stays intact.
        var textPayload = JsonNode.Parse(update.PayloadJson)?.AsObject()
            ?? throw new InvalidOperationException("Update de voz inválido.");
        var textMessage = textPayload["message"]?.AsObject()
            ?? throw new InvalidOperationException("Mensagem de voz inválida.");
        textMessage.Remove("voice");
        textMessage["text"] = transcription.Text;

        await textHandler.HandleAsync(
            new TelegramInboxUpdate(update.UpdateId, textPayload.ToJsonString()),
            cancellationToken);
    }

    private TelegramVoiceFailure? ValidateVoice(JsonElement voice)
    {
        if (voice.ValueKind != JsonValueKind.Object ||
            !voice.TryGetProperty("file_id", out var fileId) ||
            fileId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(fileId.GetString()) ||
            !voice.TryGetProperty("duration", out var duration) ||
            duration.ValueKind != JsonValueKind.Number ||
            !duration.TryGetInt32(out var seconds) || seconds <= 0)
        {
            return TelegramVoiceFailure.InvalidFile;
        }

        if (seconds > limits.MaxDurationSeconds)
        {
            return TelegramVoiceFailure.DurationLimitExceeded;
        }

        if (voice.TryGetProperty("file_size", out var size))
        {
            if (size.ValueKind != JsonValueKind.Number ||
                !size.TryGetInt64(out var bytes) || bytes <= 0)
            {
                return TelegramVoiceFailure.InvalidFile;
            }

            if (bytes > limits.MaxBytes)
            {
                return TelegramVoiceFailure.SizeLimitExceeded;
            }
        }

        // MIME and duration from Telegram are hints, not proof of decoded content.
        // Actual format and duration must also be validated by the transcriber.
        return null;
    }
}
