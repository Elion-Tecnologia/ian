using System.Data;
using System.Text.Json;
using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Ian.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresPendingConversationRepository(
    IanDbContext db,
    IOptions<TelegramOptions> telegram) : IPendingConversationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PendingIntentRepositoryStartResult> StartAsync(
        SolicitacaoPendente request,
        bool replacementConfirmed,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(request.TelegramUserId))
            return new(PendingIntentStartStatus.Invalid, null);
        await using var transaction = await BeginLockedAsync(request.TelegramUserId, cancellationToken);
        var current = await db.SolicitacoesPendentes.SingleOrDefaultAsync(
            item => item.TelegramUserId == request.TelegramUserId && item.Estado == EstadoSolicitacaoPendente.Pendente,
            cancellationToken);

        if (current is not null && current.ExpiraEmUtc <= nowUtc)
        {
            current.Estado = EstadoSolicitacaoPendente.Expirada;
            current.AtualizadaEmUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
            current = null;
        }
        else if (current is not null && !replacementConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(PendingIntentStartStatus.ReplacementConfirmationRequired, current);
        }

        if (current is not null)
        {
            current.Estado = EstadoSolicitacaoPendente.Cancelada;
            current.AtualizadaEmUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
        }

        db.SolicitacoesPendentes.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PendingIntentStartStatus.Started, request);
    }

    public async Task<SolicitacaoPendente?> GetActiveAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(telegramUserId)) return null;
        await using var transaction = await BeginLockedAsync(telegramUserId, cancellationToken);
        var current = await db.SolicitacoesPendentes.SingleOrDefaultAsync(
            item => item.TelegramUserId == telegramUserId && item.Estado == EstadoSolicitacaoPendente.Pendente,
            cancellationToken);
        if (current is not null && current.ExpiraEmUtc <= nowUtc)
        {
            current.Estado = EstadoSolicitacaoPendente.Expirada;
            current.AtualizadaEmUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
            current = null;
        }
        await transaction.CommitAsync(cancellationToken);
        return current;
    }

    public async Task<PendingIntentUpdateStatus> UpdateAsync(
        long telegramUserId,
        long requestId,
        string collectedFieldsJson,
        string missingFieldsJson,
        string candidateIdsJson,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(telegramUserId)) return PendingIntentUpdateStatus.NotFound;
        await using var transaction = await BeginLockedAsync(telegramUserId, cancellationToken);
        var current = await FindPendingAsync(telegramUserId, requestId, cancellationToken);
        var status = GetWritableStatus(current, nowUtc);
        if (status != PendingIntentUpdateStatus.Updated)
        {
            if (status == PendingIntentUpdateStatus.Expired) await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return status;
        }

        current!.DadosColetadosJson = collectedFieldsJson;
        current.CamposFaltantesJson = missingFieldsJson;
        current.CandidatosJson = candidateIdsJson;
        current.AtualizadaEmUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PendingIntentUpdateStatus.Updated;
    }

    public async Task<PendingIntentUpdateStatus> SelectCandidateAsync(
        long telegramUserId,
        long requestId,
        string field,
        long candidateId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(telegramUserId)) return PendingIntentUpdateStatus.InvalidCandidate;
        await using var transaction = await BeginLockedAsync(telegramUserId, cancellationToken);
        var current = await FindPendingAsync(telegramUserId, requestId, cancellationToken);
        var status = GetWritableStatus(current, nowUtc);
        if (status != PendingIntentUpdateStatus.Updated)
        {
            if (status == PendingIntentUpdateStatus.Expired) await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return status;
        }

        var candidates = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(current!.CandidatosJson, JsonOptions) ?? [];
        if (!candidates.TryGetValue(field, out var ids) || !ids.Contains(candidateId))
        {
            await transaction.CommitAsync(cancellationToken);
            return PendingIntentUpdateStatus.InvalidCandidate;
        }

        var collected = JsonSerializer.Deserialize<Dictionary<string, string>>(current.DadosColetadosJson, JsonOptions) ?? [];
        collected[$"{field}Id"] = candidateId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        candidates.Remove(field);
        var missing = JsonSerializer.Deserialize<List<string>>(current.CamposFaltantesJson, JsonOptions) ?? [];
        missing.RemoveAll(name => name == field || name == $"{field}Id");
        current.DadosColetadosJson = JsonSerializer.Serialize(collected, JsonOptions);
        current.CamposFaltantesJson = JsonSerializer.Serialize(missing, JsonOptions);
        current.CandidatosJson = JsonSerializer.Serialize(candidates, JsonOptions);
        current.AtualizadaEmUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PendingIntentUpdateStatus.Updated;
    }

    public async Task<PendingIntentUpdateStatus> CancelAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(telegramUserId)) return PendingIntentUpdateStatus.NotFound;
        await using var transaction = await BeginLockedAsync(telegramUserId, cancellationToken);
        var current = await db.SolicitacoesPendentes.SingleOrDefaultAsync(
            item => item.TelegramUserId == telegramUserId && item.Estado == EstadoSolicitacaoPendente.Pendente,
            cancellationToken);
        var status = GetWritableStatus(current, nowUtc);
        if (status == PendingIntentUpdateStatus.Updated)
        {
            current!.Estado = EstadoSolicitacaoPendente.Cancelada;
            current.AtualizadaEmUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (status == PendingIntentUpdateStatus.Expired)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return status == PendingIntentUpdateStatus.Updated
            ? PendingIntentUpdateStatus.Cancelled
            : status;
    }

    private Task<SolicitacaoPendente?> FindPendingAsync(long userId, long requestId, CancellationToken token) =>
        db.SolicitacoesPendentes.SingleOrDefaultAsync(
            item => item.Id == requestId && item.TelegramUserId == userId && item.Estado == EstadoSolicitacaoPendente.Pendente,
            token);

    private bool IsAuthorized(long telegramUserId) =>
        telegramUserId > 0 && telegramUserId == telegram.Value.AdministratorUserId;

    private static PendingIntentUpdateStatus GetWritableStatus(SolicitacaoPendente? current, DateTimeOffset nowUtc)
    {
        if (current is null) return PendingIntentUpdateStatus.NotFound;
        if (current.ExpiraEmUtc <= nowUtc)
        {
            current.Estado = EstadoSolicitacaoPendente.Expirada;
            current.AtualizadaEmUtc = nowUtc;
            return PendingIntentUpdateStatus.Expired;
        }
        return PendingIntentUpdateStatus.Updated;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginLockedAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({telegramUserId}::text, 790));", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
