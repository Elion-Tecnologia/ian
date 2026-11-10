namespace Ian.Application.Telegram;

public sealed record TelegramVoiceLimits(
    int MaxBytes = 5 * 1024 * 1024,
    int MaxDurationSeconds = 120,
    int TimeoutSeconds = 60)
{
    public bool IsValid() =>
        MaxBytes is > 0 and <= 20_000_000 &&
        MaxDurationSeconds is > 0 and <= 3600 &&
        TimeoutSeconds is > 0 and <= 300;
}
