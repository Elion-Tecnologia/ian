using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ian.Infrastructure.Persistence;

public static class PostgresAgendaLock
{
    // Shared with migrations. All agenda writers use this transaction-scoped key.
    public const int Namespace = 790;
    public const int Key = 1;

    public static async Task AcquireAsync(IanDbContext context, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("O lock da agenda exige uma transação existente.");
        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("A agenda exige isolamento READ COMMITTED.");

        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s';", cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({Namespace}, {Key});", cancellationToken);
    }
}
