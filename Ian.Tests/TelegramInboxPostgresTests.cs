using Ian.Application.Telegram;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class TelegramInboxPostgresTests
{
    private const long TestUpdateId = 9_100_140_001;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "IAN_TEST_POSTGRES_CONNECTION_STRING")!;

    [PostgresFact]
    public async Task ConcurrentConsumersClaimUpdateOnlyOnce()
    {
        await using var db = await CreateDatabaseAsync();
        await ResetInboxAsync(db);
        await SeedAsync(db);

        await using var source =
            NpgsqlDataSource.Create(ConnectionString);

        var first = new PostgresTelegramInboxProcessingStore(source);
        var second = new PostgresTelegramInboxProcessingStore(source);

        var claims = await Task.WhenAll(
            first.TryClaimAsync(TimeSpan.FromMinutes(2), default),
            second.TryClaimAsync(TimeSpan.FromMinutes(2), default));

        Assert.Single(
            claims,
            claim => claim?.Update.UpdateId == TestUpdateId);

        Assert.Single(
            claims,
            claim => claim is null);
    }

    [PostgresFact]
    public async Task ExpiredLeaseIsRecoveredByAnotherConsumer()
    {
        await using var db = await CreateDatabaseAsync();
        await ResetInboxAsync(db);
        await SeedAsync(db);

        await using var source =
            NpgsqlDataSource.Create(ConnectionString);

        var first = new PostgresTelegramInboxProcessingStore(source);
        var second = new PostgresTelegramInboxProcessingStore(source);

        var original = await first.TryClaimAsync(
            TimeSpan.FromMinutes(2), default);

        Assert.NotNull(original);

        // Simula o vencimento do lease depois da queda do processo.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE telegram_updates_inbox
            SET "LockAteUtc" = now() - interval '1 second'
            WHERE "UpdateId" = {TestUpdateId};
            """);

        var recovered = await second.TryClaimAsync(
            TimeSpan.FromMinutes(2), default);

        Assert.NotNull(recovered);
        Assert.Equal(TestUpdateId, recovered.Update.UpdateId);
        Assert.Equal(2, recovered.Attempt);
        Assert.NotEqual(original.LeaseToken, recovered.LeaseToken);

        // O consumidor antigo não pode confirmar a nova tentativa.
        Assert.False(await first.CompleteAsync(
            TestUpdateId,
            original.LeaseToken,
            "Authorized",
            default));
    }

    [PostgresFact]
    public async Task RetryRespectsScheduledTime()
    {
        await using var db = await CreateDatabaseAsync();
        await ResetInboxAsync(db);
        await SeedAsync(db);

        await using var source =
            NpgsqlDataSource.Create(ConnectionString);

        var store = new PostgresTelegramInboxProcessingStore(source);

        var first = await store.TryClaimAsync(
            TimeSpan.FromMinutes(2), default);

        Assert.NotNull(first);

        Assert.True(await store.FailAsync(
            TestUpdateId,
            first.LeaseToken,
            TimeSpan.FromMinutes(5),
            "ProcessingFailed",
            default));

        Assert.Null(await store.TryClaimAsync(
            TimeSpan.FromMinutes(2), default));

        // Simula a chegada do horário do retry.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE telegram_updates_inbox
            SET "ProximaTentativaEmUtc" = now() - interval '1 second'
            WHERE "UpdateId" = {TestUpdateId};
            """);

        var second = await store.TryClaimAsync(
            TimeSpan.FromMinutes(2), default);

        Assert.NotNull(second);
        Assert.Equal(2, second.Attempt);
    }

    [PostgresFact]
    public async Task ConcurrentBusinessOperationCommitsOnlyOnce()
    {
        await using var db = await CreateDatabaseAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ian_t08_test_effects
            ("UpdateId" bigint PRIMARY KEY);
            """);

        await ResetInboxAsync(db);

        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM ian_t08_test_effects;");

        await SeedAsync(db);

        await using var firstDb = await CreateDatabaseAsync();
        await using var secondDb = await CreateDatabaseAsync();

        var first = new TelegramBusinessOperationExecutor(firstDb);
        var second = new TelegramBusinessOperationExecutor(secondDb);

        async Task<bool> ExecuteAsync(
            TelegramBusinessOperationExecutor executor)
        {
            return await executor.ExecuteOnceAsync(
                TestUpdateId,
                "agendamento:create",
                async (context, cancellationToken) =>
                {
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        INSERT INTO ian_t08_test_effects ("UpdateId")
                        VALUES ({TestUpdateId});
                        """,
                        cancellationToken);
                });
        }

        var results = await Task.WhenAll(
            ExecuteAsync(first),
            ExecuteAsync(second));

        Assert.Single(results, executed => executed);
        Assert.Single(results, executed => !executed);

        await using var verificationSource =
     NpgsqlDataSource.Create(ConnectionString);

        await using var connection =
            await verificationSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM ian_t08_test_effects
            WHERE "UpdateId" = @updateId;
            """,
            connection);

        command.Parameters.AddWithValue("updateId", TestUpdateId);

        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync());

        Assert.Equal(1, count);
    }

    [PostgresFact]
    public async Task DatabaseUnavailableDoesNotLosePendingUpdate()
    {
        await using var db = await CreateDatabaseAsync();
        await ResetInboxAsync(db);
        await SeedAsync(db);

        await using var unavailableSource = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=65432;Database=unavailable;" +
            "Username=test;Password=test;Timeout=1");

        var unavailableStore =
            new PostgresTelegramInboxProcessingStore(unavailableSource);

        await Assert.ThrowsAnyAsync<Exception>(
            () => unavailableStore.TryClaimAsync(
                TimeSpan.FromMinutes(2), default));

        var pending = await db.TelegramUpdatesInbox
            .AsNoTracking()
            .SingleAsync(item => item.UpdateId == TestUpdateId);

        Assert.Equal(EstadoTelegramUpdate.Received, pending.Estado);

        await using var recoveredSource =
            NpgsqlDataSource.Create(ConnectionString);

        var recoveredStore =
            new PostgresTelegramInboxProcessingStore(recoveredSource);

        var claimed = await recoveredStore.TryClaimAsync(
            TimeSpan.FromMinutes(2), default);

        Assert.NotNull(claimed);
        Assert.Equal(TestUpdateId, claimed.Update.UpdateId);
    }

    [PostgresFact]
    public async Task FailedBusinessTransactionDoesNotConsumeIdempotencyKey()
    {
        await using var db = await CreateDatabaseAsync();
        await ResetInboxAsync(db);
        await SeedAsync(db);

        await using (var failedDb = await CreateDatabaseAsync())
        {
            var executor = new TelegramBusinessOperationExecutor(
                failedDb);

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await executor.ExecuteOnceAsync(
                        TestUpdateId,
                        "agendamento:create",
                        (_, _) => throw new InvalidOperationException(
                            "Falha simulada."));
                });
        }

        var existing = await db.TelegramBusinessOperations
            .AsNoTracking()
            .CountAsync(operation =>
                operation.UpdateId == TestUpdateId &&
                operation.OperationKey == "agendamento:create");

        Assert.Equal(0, existing);

        await using var retryDb = await CreateDatabaseAsync();

        var retryExecutor = new TelegramBusinessOperationExecutor(
            retryDb);

        var succeeded = await retryExecutor.ExecuteOnceAsync(
            TestUpdateId,
            "agendamento:create",
            (_, _) => Task.CompletedTask);

        Assert.True(succeeded);
    }

    [PostgresFact]
    public async Task DuplicateWebhookStorageKeepsSingleRow()
    {
        await using var db = await CreateDatabaseAsync();
        await ResetInboxAsync(db);

        await using var firstDb = await CreateDatabaseAsync();
        await using var secondDb = await CreateDatabaseAsync();

        var update = new TelegramInboxUpdate(
            TestUpdateId,
            """{"update_id":9100140001}""");

        var first = new TelegramUpdateInboxWriter(firstDb);
        var second = new TelegramUpdateInboxWriter(secondDb);

        await Task.WhenAll(
            first.StoreAsync(update, default),
            second.StoreAsync(update, default));

        var count = await db.TelegramUpdatesInbox
            .AsNoTracking()
            .CountAsync(item => item.UpdateId == TestUpdateId);

        Assert.Equal(1, count);
    }

    private static async Task<IanDbContext> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<IanDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        var db = new IanDbContext(options);

        await db.Database.MigrateAsync();

        return db;
    }

    private static async Task ResetInboxAsync(IanDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM telegram_business_operations;");

        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM telegram_updates_inbox;");
    }

    private static async Task SeedAsync(IanDbContext db)
    {
        db.TelegramUpdatesInbox.Add(new TelegramUpdateInbox
        {
            UpdateId = TestUpdateId,
            PayloadJson = """{"update_id":9100140001}""",
            Estado = EstadoTelegramUpdate.Received,
            RecebidoEmUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(
                "IAN_TEST_POSTGRES_CONNECTION_STRING")))
        {
            Skip = "Requer o banco ian_t08_tests configurado.";
        }
    }
}