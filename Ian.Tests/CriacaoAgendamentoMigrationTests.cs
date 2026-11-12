using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class CriacaoAgendamentoMigrationTests
{
    private const string Catalogo = "20261111144340_PopularCatalogoTiposServico";

    [PostgresFact]
    public async Task UpgradePreservaOperacoesAnterioresEAdicionaVinculoOpcionalComFk()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Catalogo);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO telegram_updates_inbox
                ("UpdateId", "PayloadJson", "Estado", "RecebidoEmUtc", "Tentativas")
            VALUES
                (31001, '{{"update_id":31001}}', 'Completed', '2030-01-01 12:00Z', 0);

            INSERT INTO telegram_business_operations
                ("UpdateId", "OperationKey", "ExecutadoEmUtc")
            VALUES
                (31001, 'synthetic:before-31', '2030-01-01 12:01Z');
            """);

        await db.GetService<IMigrator>().MigrateAsync("20261111194044_VincularOperacaoAoAgendamento");

        Assert.Equal(8, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var operation = await db.TelegramBusinessOperations.AsNoTracking().SingleAsync();
        Assert.Equal("synthetic:before-31", operation.OperationKey);
        Assert.Null(operation.AgendamentoId);

        var foreignKey = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """
                UPDATE telegram_business_operations
                SET "AgendamentoId" = 999999
                WHERE "UpdateId" = 31001;
                """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignKey.SqlState);
    }
}
