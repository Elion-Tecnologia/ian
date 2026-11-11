using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class TelegramBusinessOperationExecutor(
    IanDbContext dbContext)
{
    public async Task<bool> ExecuteOnceAsync(
        long updateId,
        string operationKey,
        Func<IanDbContext, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (updateId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(updateId));
        }

        if (string.IsNullOrWhiteSpace(operationKey) ||
            operationKey.Length > 80 ||
            operationKey.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character != ':' &&
                character != '-' &&
                character != '_'))
        {
            throw new ArgumentException(
                "Chave de operação inválida.",
                nameof(operationKey));
        }

        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("O executor deve ser o único proprietário da transação.");
        if (dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("A operação exige um contexto sem alterações pendentes.");
        // Do not reuse tracked snapshots from a previous operation after acquiring the agenda lock.
        dbContext.ChangeTracker.Clear();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            // A inserção e a operação de negócio precisam ser
            // confirmadas na mesma transação PostgreSQL.
            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO telegram_business_operations
                    ("UpdateId", "OperationKey", "ExecutadoEmUtc")
                VALUES
                    ({updateId}, {operationKey}, now())
                ON CONFLICT ("UpdateId", "OperationKey") DO NOTHING;
                """,
                cancellationToken);

            if (inserted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await action(dbContext, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { dbContext.ChangeTracker.Clear(); }
            throw;
        }
    }
}
