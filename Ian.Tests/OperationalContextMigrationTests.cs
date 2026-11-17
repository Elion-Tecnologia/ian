using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class OperationalContextMigrationTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentSchemaSupportsOperationalContextAndPreservesPreviousTurns(bool upgrade)
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var readiness = new PostgresSchemaReadiness(db);
        Assert.False(await readiness.IsReadyAsync());
        if (upgrade)
        {
            await db.GetService<IMigrator>().MigrateAsync("20261112045401_PersistirRetentativasRespostas");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO telegram_updates_inbox ("UpdateId", "PayloadJson", "Estado", "RecebidoEmUtc", "Tentativas")
                VALUES (117, '{{"update_id":117}}', 'Completed', now(), 0);
                INSERT INTO telegram_agent_turns ("UpdateId", "TelegramUserId", "LogicalReplyKey", "SendState", "SendAttempts", "ResponseText")
                VALUES (117, 42, 'private-reply:117:42', 'Pending', 0, 'Resposta preservada');
                """);
            Assert.False(await readiness.IsReadyAsync());
            var missing = await Assert.ThrowsAsync<PostgresException>(() => db.Set<TelegramAgentTurn>().AsNoTracking().ToListAsync());
            Assert.Equal(PostgresErrorCodes.UndefinedColumn, missing.SqlState);
        }
        await db.Database.MigrateAsync();
        Assert.True(await readiness.IsReadyAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        if (upgrade)
        {
            var old = await db.Set<TelegramAgentTurn>().SingleAsync();
            Assert.Equal("Resposta preservada", old.ResponseText);
            Assert.Null(old.OperationalContextJson);
            old.OperationalContextJson = "{\"clienteId\":117}";
            await db.SaveChangesAsync();
        }
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(upgrade ? 1 : 0, await db.Set<TelegramAgentTurn>().CountAsync());
        if (upgrade) Assert.Contains("117", (await db.Set<TelegramAgentTurn>().SingleAsync()).OperationalContextJson!);
    }
}
