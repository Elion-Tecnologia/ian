namespace Ian.Application.Telegram;

public sealed record TelegramClaimedUpdate(
    TelegramInboxUpdate Update,
    Guid LeaseToken,
    int Attempt);

public interface ITelegramInboxProcessingStore
{
    Task<TelegramClaimedUpdate?> TryClaimAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        long updateId,
        Guid leaseToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        long updateId,
        Guid leaseToken,
        string result,
        CancellationToken cancellationToken);

    Task<bool> FailAsync(
        long updateId,
        Guid leaseToken,
        TimeSpan? retryDelay,
        string errorCode,
        CancellationToken cancellationToken);
}