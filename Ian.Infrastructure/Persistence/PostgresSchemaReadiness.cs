using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

// Read-only deployment guard: migrations must be applied explicitly by the operator.
public sealed class PostgresSchemaReadiness(IanDbContext db)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        !db.Database.HasPendingModelChanges() &&
        !(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
}
