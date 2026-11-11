namespace Ian.Application.Telegram;

public enum TelegramTextSendStatus
{
    Accepted,
    UnauthorizedRecipient,
    InvalidMessage,
    InvalidConfiguration,
    RateLimited,
    ServerFailure,
    AuthenticationFailure,
    Rejected,
    Timeout,
    TransportFailure,
    InvalidResponse,
    UnexpectedFailure
}

/// <summary>Acceptance by Telegram is not evidence of delivery or human reading.</summary>
public sealed record TelegramTextSendResult(
    TelegramTextSendStatus Status,
    int Attempts,
    long? MessageId = null,
    TimeSpan? RetryAfter = null,
    bool DeliveryUncertain = false)
{
    public bool IsAccepted => Status == TelegramTextSendStatus.Accepted;
    public override string ToString() => Status.ToString();
}

public interface ITelegramTextSender
{
    /// <summary>
    /// Sends a plain text reply to the authorized private text update. The host must invoke
    /// this after inbox persistence and any required business commit. Failures never imply
    /// business success; uncertain sends must not trigger blind retries or replay mutations.
    /// Caller cancellation propagates as OperationCanceledException.
    /// </summary>
    Task<TelegramTextSendResult> SendAsync(
        TelegramInboxUpdate source, string text, CancellationToken cancellationToken = default);
}
