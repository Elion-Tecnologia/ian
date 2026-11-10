namespace Ian.Application.Telegram;

public enum TelegramVoiceFailure
{
    InvalidFile,
    SizeLimitExceeded,
    DurationLimitExceeded,
    Timeout,
    RateLimited,
    DownloadUnavailable,
    TranscriptionUnavailable,
    EmptyTranscription,
    NeedsClarification
}
