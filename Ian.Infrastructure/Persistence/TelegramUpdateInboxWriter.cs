
using Ian.Application.Telegram;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Infrastructure.Persistence;

public sealed class TelegramUpdateInboxWriter(
    IanDbContext dbContext) : ITelegramUpdateInboxWriter
{
    public async Task StoreAsync(
        TelegramInboxUpdate update,
        CancellationToken cancellationToken)
    {
        var entity = new TelegramUpdateInbox
        {
            UpdateId = update.UpdateId,
            PayloadJson = update.PayloadJson,
            Estado = EstadoTelegramUpdate.Received,
            RecebidoEmUtc = DateTimeOffset.UtcNow
        };

        dbContext.TelegramUpdatesInbox.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_telegram_updates_inbox"
            })
        {
            // A atualização já está armazenada.
            // A reentrega não deve causar uma segunda inserção.
            dbContext.Entry(entity).State = EntityState.Detached;
        }
    }
}
