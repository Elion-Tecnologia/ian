namespace Ian.Infrastructure.Telegram;

public sealed class TelegramInboxProcessingOptions
{
    public TimeSpan LeaseDuration { get; set; }
        = TimeSpan.FromMinutes(2);

    public TimeSpan HeartbeatInterval { get; set; }
        = TimeSpan.FromSeconds(20);

    public TimeSpan IdleDelay { get; set; }
        = TimeSpan.FromSeconds(2);

    public TimeSpan ErrorDelay { get; set; }
        = TimeSpan.FromSeconds(5);

    public TimeSpan RetryBaseDelay { get; set; }
        = TimeSpan.FromSeconds(10);

    public TimeSpan RetryMaxDelay { get; set; }
        = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 5;

    public bool IsValid()
    {
        return LeaseDuration > TimeSpan.Zero
            && HeartbeatInterval > TimeSpan.Zero
            && HeartbeatInterval * 2 < LeaseDuration
            && IdleDelay > TimeSpan.Zero
            && ErrorDelay > TimeSpan.Zero
            && RetryBaseDelay > TimeSpan.Zero
            && RetryMaxDelay >= RetryBaseDelay
            && MaxAttempts >= 1;
    }
}