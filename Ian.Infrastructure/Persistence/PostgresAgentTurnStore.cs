using System.Text.Json;
using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresAgentTurnStore(IanDbContext db, NpgsqlDataSource dataSource, TimeProvider? clock = null) : IAgentTurnStore
{
    public async Task<IAsyncDisposable> LockAsync(long userId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@user::text, 440))", connection)
            { CommandTimeout = 65 };
            command.Parameters.AddWithValue("user", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new UserLock(connection, userId);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<TelegramAgentTurn> GetOrCreateAsync(long updateId, long userId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO telegram_agent_turns ("UpdateId", "TelegramUserId", "LogicalReplyKey", "SendState", "SendAttempts")
            VALUES ({updateId}, {userId}, {FormattableString.Invariant($"private-reply:{updateId}:{userId}")}, 'Pending', 0) ON CONFLICT ("UpdateId") DO NOTHING
            """, cancellationToken);
        var turn = await db.Set<TelegramAgentTurn>().AsNoTracking().SingleAsync(t => t.UpdateId == updateId, cancellationToken);
        if (turn.TelegramUserId != userId) throw new InvalidOperationException("Ator do turno inválido.");
        return turn;
    }

    public async Task<bool> FreezeCommandAsync(long updateId, string commandJson, CancellationToken cancellationToken)
    {
        await db.Set<TelegramAgentTurn>().Where(t => t.UpdateId == updateId && t.CommandJson == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CommandJson, commandJson), cancellationToken);
        var stored = await db.Set<TelegramAgentTurn>().AsNoTracking().Where(t => t.UpdateId == updateId)
            .Select(t => t.CommandJson).SingleAsync(cancellationToken);
        // jsonb can reorder keys and whitespace; compare canonical parsed requests.
        return System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(stored ?? "null"), System.Text.Json.Nodes.JsonNode.Parse(commandJson));
    }

    public Task<long?> GetCommittedAppointmentAsync(long updateId, CancellationToken cancellationToken) =>
        db.TelegramBusinessOperations.AsNoTracking().Where(o => o.UpdateId == updateId && o.AgendamentoId != null)
            .Select(o => o.AgendamentoId).FirstOrDefaultAsync(cancellationToken);

    public async Task<StoredConfirmation?> GetConfirmationAsync(long userId, long pendingId, CancellationToken cancellationToken)
    {
        var json = await db.SolicitacoesPendentes.AsNoTracking()
            .Where(p => p.TelegramUserId == userId && p.Id == pendingId && p.Estado == EstadoSolicitacaoPendente.Pendente)
            .Select(p => p.ConfirmacaoJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<StoredConfirmation>(json, OperationConfirmationPolicy.JsonOptions);
    }

    public Task<string?> GetPreviousResponseAsync(long userId, long updateId, CancellationToken cancellationToken) =>
        db.Set<TelegramAgentTurn>().AsNoTracking().Where(t => t.TelegramUserId == userId && t.UpdateId < updateId && t.ResponseText != null)
            .OrderByDescending(t => t.UpdateId).Select(t => t.ResponseText).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AgentConversationTurn>> GetHistoryAsync(long userId, long updateId, CancellationToken cancellationToken)
    {
        var rows = await (from turn in db.Set<TelegramAgentTurn>().AsNoTracking()
            join inbox in db.TelegramUpdatesInbox.AsNoTracking() on turn.UpdateId equals inbox.UpdateId
            where turn.TelegramUserId == userId && turn.UpdateId < updateId && turn.ResponseText != null
            orderby turn.UpdateId descending
            select new { inbox.PayloadJson, turn.ResponseText }).Take(3).ToListAsync(cancellationToken);
        return rows.AsEnumerable().Reverse().Select(row =>
        {
            using var json = JsonDocument.Parse(row.PayloadJson);
            var text = json.RootElement.GetProperty("message").GetProperty("text").GetString() ?? "";
            var resposta = row.ResponseText ?? "";
            return new AgentConversationTurn(text[..Math.Min(text.Length, 500)], resposta[..Math.Min(resposta.Length, 1000)]);
        }).ToArray();
    }

    public Task CompletePendingAsync(long userId, CancellationToken cancellationToken) =>
        db.SolicitacoesPendentes.Where(p => p.TelegramUserId == userId && p.Estado == EstadoSolicitacaoPendente.Pendente)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoSolicitacaoPendente.Confirmada), cancellationToken);

    public Task SaveResponseAsync(long updateId, string response, CancellationToken cancellationToken) =>
        db.Set<TelegramAgentTurn>().Where(t => t.UpdateId == updateId && t.ResponseText == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ResponseText, response)
                .SetProperty(t => t.NextSendAttemptAtUtc, (clock ?? TimeProvider.System).GetUtcNow()), cancellationToken);

    private sealed class UserLock(NpgsqlConnection connection, long userId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@user::text, 440))", connection);
                command.Parameters.AddWithValue("user", userId);
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
