using System.Text.Json;

namespace Ian.Application.Telegram;

public sealed class TelegramUpdateDispatcher(
    ITelegramAuthorizedUpdateHandler authorizedHandler,
    ITelegramGroupEventHandler groupEventHandler)
{
    public async Task<TelegramAuthorizationDecision> DispatchAsync(
        TelegramInboxUpdate update,
        long administratorUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(update.PayloadJson);
        }
        catch (JsonException)
        {
            return TelegramAuthorizationDecision.Denied;
        }

        using (document)
        {
            var root = document.RootElement;

            var decision = TelegramUpdateAuthorization.Evaluate(
                root,
                administratorUserId);

            switch (decision)
            {
                case TelegramAuthorizationDecision.Authorized:
                    await authorizedHandler.HandleAsync(
                        update,
                        cancellationToken);
                    break;

                case TelegramAuthorizationDecision.NonCommandEvent:
                    if (TelegramUpdateAuthorization.TryGetGroupCandidate(
                            root, out var candidate) &&
                        candidate is not null)
                    {
                        await groupEventHandler.HandleCandidateAsync(
                            candidate,
                            cancellationToken);
                    }
                    break;

                case TelegramAuthorizationDecision.Denied:
                    break;

                default:
                    throw new InvalidOperationException(
                        "Decisão de autorização desconhecida.");
            }

            return decision;
        }
    }
}