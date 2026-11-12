namespace Ian.Application.Telegram;

public sealed record TelegramReplyClaim(long UpdateId, long UserId, string Text, string PayloadJson, int Number, Guid Token);

public interface ITelegramReplyOutbox
{
    Task<TelegramReplyClaim?> ClaimAsync(DateTimeOffset now, long? updateId, CancellationToken ct);
    Task<bool> FinishAsync(TelegramReplyClaim claim, TelegramTextSendResult result, DateTimeOffset now, CancellationToken ct);
}

public static class TelegramReplyDeliveryPolicy
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public static (string State, DateTimeOffset? Next) Classify(TelegramTextSendResult result, int number, DateTimeOffset now)
    {
        if (result.IsAccepted && result.MessageId is > 0) return ("Accepted", null);
        if (result.DeliveryUncertain || result.IsAccepted) return ("Uncertain", null);
        var retryable = result.Status == TelegramTextSendStatus.RateLimited ||
            result.Attempts == 0 && result.Status is TelegramTextSendStatus.Timeout or TelegramTextSendStatus.TransportFailure or TelegramTextSendStatus.ServerFailure;
        if (!retryable || number >= MaxAttempts) return ("Failed", null);
        var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Max(0, number - 1))));
        if (result.RetryAfter is { } after && after > delay) delay = after;
        return ("Failed", delay > DateTimeOffset.MaxValue - now ? null : now.Add(delay));
    }
}
