namespace Ian.Domain.Entities;

// Delivery evidence only; no message text, credentials or general business audit.
public sealed class TelegramReplyAttempt
{
    public long UpdateId { get; init; }
    public int Number { get; init; }
    public Guid Token { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? Status { get; set; }
    public int TransportAttempts { get; set; }
    public bool DeliveryUncertain { get; set; }
    public long? TelegramMessageId { get; set; }
    public long? RetryAfterSeconds { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
}
