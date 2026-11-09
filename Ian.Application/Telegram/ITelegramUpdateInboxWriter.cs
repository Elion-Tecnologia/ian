namespace Ian.Application.Telegram;

public sealed record TelegramInboxUpdate(
    long UpdateId,
    string PayloadJson);

public interface ITelegramUpdateInboxWriter
{
    Task StoreAsync(
        TelegramInboxUpdate update,
        CancellationToken cancellationToken);
}