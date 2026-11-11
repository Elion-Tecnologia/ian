namespace Ian.Infrastructure.Telegram;

public sealed class TelegramTextSendingOptions
{
    public const string SectionName = "Telegram:Sending";

    public int RequestTimeoutSeconds { get; set; } = 10;
    public int TotalTimeoutSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 3;
    public int MaxRetryDelaySeconds { get; set; } = 10;

    public bool IsValid() => RequestTimeoutSeconds is >= 1 and <= 30 &&
        TotalTimeoutSeconds >= RequestTimeoutSeconds && TotalTimeoutSeconds <= 60 &&
        MaxAttempts is >= 1 and <= 3 && MaxRetryDelaySeconds is >= 1 and <= 30;
}
