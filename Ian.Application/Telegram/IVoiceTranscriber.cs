namespace Ian.Application.Telegram;

public interface IVoiceTranscriber
{
    // The adapter must reject invalid/overlong audio and report uncertainty.
    // It must not execute tools, infer missing commands or retain the audio.
    Task<VoiceTranscriptionResult> TranscribeAsync(
        TelegramVoiceFile file,
        int maxDurationSeconds,
        CancellationToken cancellationToken);
}

public sealed record VoiceTranscriptionResult
{
    private VoiceTranscriptionResult(string? text, TelegramVoiceFailure? failure)
    {
        Text = text;
        Failure = failure;
    }

    public string? Text { get; }
    public TelegramVoiceFailure? Failure { get; }

    public override string ToString() => Failure?.ToString() ?? "Transcribed";

    public static VoiceTranscriptionResult Transcribed(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? Rejected(TelegramVoiceFailure.EmptyTranscription)
            : new(text.Trim(), null);

    public static VoiceTranscriptionResult Rejected(TelegramVoiceFailure failure) =>
        new(null, failure);
}
