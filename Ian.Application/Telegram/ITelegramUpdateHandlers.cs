namespace Ian.Application.Telegram;

public interface ITelegramAuthorizedUpdateHandler
{
    Task HandleAsync(
        TelegramInboxUpdate update,
        CancellationToken cancellationToken);
}

public interface ITelegramGroupEventHandler
{
    Task HandleCandidateAsync(TelegramGroupEventCandidate candidate, CancellationToken cancellationToken);
}