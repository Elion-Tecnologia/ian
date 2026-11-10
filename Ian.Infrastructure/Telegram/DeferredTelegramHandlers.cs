using Ian.Application.Telegram;

namespace Ian.Infrastructure.Telegram;

public sealed class TelegramHandlerUnavailableException : Exception
{
    public TelegramHandlerUnavailableException()
        : base("O handler Telegram ainda não está disponível.")
    {
    }
}

public sealed class DeferredTelegramHandlers :
    ITelegramAuthorizedUpdateHandler,
    ITelegramGroupEventHandler
{
    public Task HandleAsync(
        TelegramInboxUpdate update,
        CancellationToken cancellationToken)
    {
        throw new TelegramHandlerUnavailableException();
    }

    public Task HandleCandidateAsync(
        TelegramGroupEventCandidate candidate,
        CancellationToken cancellationToken)
    {
        throw new TelegramHandlerUnavailableException();
    }
}