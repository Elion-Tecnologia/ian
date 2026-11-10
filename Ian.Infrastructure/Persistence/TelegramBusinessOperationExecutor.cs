using Ian.Domain.Entities;
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

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

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
}