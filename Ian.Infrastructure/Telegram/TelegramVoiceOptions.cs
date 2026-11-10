using Ian.Application.Telegram;

namespace Ian.Infrastructure.Telegram;

public sealed class TelegramVoiceOptions
{
    public const string SectionName = "TelegramVoice";

    public int MaxBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxDurationSeconds { get; set; } = 120;
    public int TimeoutSeconds { get; set; } = 60;

    public TelegramVoiceLimits ToLimits() =>
        new(MaxBytes, MaxDurationSeconds, TimeoutSeconds);
}
