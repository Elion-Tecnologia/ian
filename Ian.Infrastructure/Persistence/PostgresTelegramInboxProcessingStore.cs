using Ian.Application.Telegram;
using Npgsql;
using NpgsqlTypes;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresTelegramInboxProcessingStore(
    NpgsqlDataSource dataSource) : ITelegramInboxProcessingStore
{
    public async Task<TelegramClaimedUpdate?> TryClaimAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration));
        }

        const string sql = """
            WITH candidate AS (
                SELECT "UpdateId"
                FROM telegram_updates_inbox
                WHERE
                    "Estado" = 'Received'
                    OR (
                        "Estado" = 'FailedRetryable'
                        AND "ProximaTentativaEmUtc" <= now()
                    )
                    OR (
                        "Estado" = 'Processing'
                        AND "LockAteUtc" <= now()
                    )
                ORDER BY "RecebidoEmUtc", "UpdateId"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE telegram_updates_inbox AS inbox
            SET
                "Estado" = 'Processing',
                "Tentativas" = inbox."Tentativas" + 1,
                "IniciadoEmUtc" = now(),
                "LockToken" = @token,
                "LockAteUtc" = now() + @lease,
                "ProximaTentativaEmUtc" = NULL,
                "UltimoErroCodigo" = NULL
            FROM candidate
            WHERE inbox."UpdateId" = candidate."UpdateId"
            RETURNING
                inbox."UpdateId",
                inbox."PayloadJson",
                inbox."Tentativas";
            """;

        var token = Guid.NewGuid();

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            sql, connection);

        command.Parameters.AddWithValue(
            "token", NpgsqlDbType.Uuid, token);

        command.Parameters.AddWithValue(
            "lease", NpgsqlDbType.Interval, leaseDuration);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var update = new TelegramInboxUpdate(
            reader.GetInt64(0),
            reader.GetString(1));

        return new TelegramClaimedUpdate(
            update,
            token,
            reader.GetInt32(2));
    }

    public async Task<bool> RenewLeaseAsync(
        long updateId,
        Guid leaseToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE telegram_updates_inbox
            SET "LockAteUtc" = now() + @lease
            WHERE "UpdateId" = @updateId
              AND "Estado" = 'Processing'
              AND "LockToken" = @token
              AND "LockAteUtc" > now();
            """;

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            sql, connection);

        AddIdentityParameters(command, updateId, leaseToken);

        command.Parameters.AddWithValue(
            "lease", NpgsqlDbType.Interval, leaseDuration);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> CompleteAsync(
        long updateId,
        Guid leaseToken,
        string result,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE telegram_updates_inbox
            SET
                "Estado" = 'Completed',
                "ConcluidoEmUtc" = now(),
                "Resultado" = @result,
                "UltimoErroCodigo" = NULL,
                "LockToken" = NULL,
                "LockAteUtc" = NULL
            WHERE "UpdateId" = @updateId
              AND "Estado" = 'Processing'
              AND "LockToken" = @token
              AND "LockAteUtc" > now();
            """;

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            sql, connection);

        AddIdentityParameters(command, updateId, leaseToken);

        command.Parameters.AddWithValue(
            "result", NpgsqlDbType.Varchar, result);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> FailAsync(
        long updateId,
        Guid leaseToken,
        TimeSpan? retryDelay,
        string errorCode,
        CancellationToken cancellationToken)
    {
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryDelay));
        }

        const string sql = """
            UPDATE telegram_updates_inbox
            SET
                "Estado" = CASE
                    WHEN @retry IS NULL THEN 'FailedPaused'
                    ELSE 'FailedRetryable'
                END,
                "ProximaTentativaEmUtc" = now() + @retry,
                "UltimoErroCodigo" = @errorCode,
                "LockToken" = NULL,
                "LockAteUtc" = NULL
            WHERE "UpdateId" = @updateId
              AND "Estado" = 'Processing'
              AND "LockToken" = @token
              AND "LockAteUtc" > now();
            """;

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            sql, connection);

        AddIdentityParameters(command, updateId, leaseToken);

        command.Parameters.Add(new NpgsqlParameter(
            "retry", NpgsqlDbType.Interval)
        {
            Value = retryDelay.HasValue
                ? retryDelay.Value
                : DBNull.Value
        });

        command.Parameters.AddWithValue(
            "errorCode", NpgsqlDbType.Varchar, errorCode);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void AddIdentityParameters(
        NpgsqlCommand command,
        long updateId,
        Guid leaseToken)
    {
        command.Parameters.AddWithValue(
            "updateId", NpgsqlDbType.Bigint, updateId);

        command.Parameters.AddWithValue(
            "token", NpgsqlDbType.Uuid, leaseToken);
    }
}