using System.Data;
using System.Text.Json;
using Ian.Application.Agenda;
using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Ian.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresOperationConfirmation(
    IanDbContext db, AgendaMutationExecutor mutations, IOptions<TelegramOptions> telegram,
    TimeProvider clock) : IOperationConfirmation
{
    public async Task<AgentToolResult> PrepareAsync(
        AgentToolCaller caller, long pendingRequestId, AlterarAgendamentoToolRequest arguments,
        CancellationToken cancellationToken = default)
    {
        if (!Authorized(caller)) return Unauthorized();
        if (!OperationConfirmationPolicy.RequiresConfirmation(arguments))
            return AgentToolResult.Succeeded("Esta operação dispensa confirmação adicional.");
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("A confirmação exige um contexto sem alterações pendentes.");
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await PostgresAgendaLock.AcquireAsync(db, cancellationToken);
        await LockConversationAsync(caller.UserId, cancellationToken);
        var pending = await FindAsync(caller.UserId, pendingRequestId, cancellationToken);
        var now = clock.GetUtcNow();
        var invalid = OperationConfirmationPolicy.ValidatePending(pending, caller.UserId, arguments, now);
        if (invalid is not null) return invalid;
        var target = await db.Agendamentos.SingleOrDefaultAsync(item => item.Id == arguments.AgendamentoId, cancellationToken);
        if (target is null || target.Estado != EstadoAgendamento.Agendado)
            return AgentToolResult.ConflictResult("Agendamento ausente ou encerrado.");
        var expires = now.Add(OperationConfirmationPolicy.Lifetime);
        if (expires > pending!.ExpiraEmUtc) expires = pending.ExpiraEmUtc;
        var confirmation = new StoredConfirmation(Guid.NewGuid(), target.Id, target.Versao,
            OperationConfirmationPolicy.Normalize(arguments), expires);
        pending.ConfirmacaoJson = JsonSerializer.Serialize(confirmation, OperationConfirmationPolicy.JsonOptions);
        pending.AtualizadaEmUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AgentToolResult.RequiresConfirmation($"Confirma o cancelamento do agendamento {target.Id}?",
            new ConfirmationChallenge(pending.Id, confirmation.ChallengeId, target.Id, target.Versao, expires));
    }

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolCaller caller, long updateId, ConfirmationAcceptance acceptance,
        Func<ConfirmedAgendaOperation, CancellationToken, Task<AgentToolResult>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (!Authorized(caller)) return Unauthorized();
        if (updateId < 0 || acceptance.PendingRequestId <= 0 || acceptance.ChallengeId == Guid.Empty)
            return AgentToolResult.ConflictResult("Confirmação inválida.");
        AgentToolResult? result = null;
        try
        {
            var executed = await mutations.ExecuteOnceAsync(updateId, $"confirmation:{acceptance.PendingRequestId}", async (_, token) =>
            {
                // Authorization and current state are checked again inside the writing transaction.
                if (!Authorized(caller)) throw new RejectedConfirmation(Unauthorized());
                await LockConversationAsync(caller.UserId, token);
                var pending = await FindAsync(caller.UserId, acceptance.PendingRequestId, token);
                var now = clock.GetUtcNow();
                var invalid = OperationConfirmationPolicy.ValidatePending(pending, caller.UserId, acceptance.Arguments, now);
                if (invalid is not null) throw new RejectedConfirmation(invalid);
                var confirmation = pending!.ConfirmacaoJson is null ? null :
                    JsonSerializer.Deserialize<StoredConfirmation>(pending.ConfirmacaoJson, OperationConfirmationPolicy.JsonOptions);
                var target = await db.Agendamentos.SingleOrDefaultAsync(
                    item => item.Id == acceptance.Arguments.AgendamentoId, token);
                invalid = OperationConfirmationPolicy.ValidateAcceptance(confirmation, acceptance, target, now);
                if (invalid is not null) throw new RejectedConfirmation(invalid);
                result = await mutation(new(target!.Id, target.Versao, confirmation!.Arguments), token);
                if (result.Outcome != AgentToolOutcome.Success) throw new RejectedConfirmation(result);
                // Check the deadline at the final write boundary, including time spent by the use case.
                if (confirmation.ExpiresAtUtc <= clock.GetUtcNow() || pending.ExpiraEmUtc <= clock.GetUtcNow())
                    throw new RejectedConfirmation(AgentToolResult.ConflictResult("A confirmação expirou. Refaça o pedido."));
                pending.Estado = EstadoSolicitacaoPendente.Confirmada;
                pending.AtualizadaEmUtc = clock.GetUtcNow();
            }, cancellationToken);
            return executed ? result! : AgentToolResult.ConflictResult("Esta confirmação já foi processada.");
        }
        catch (RejectedConfirmation rejected) { return rejected.Result; }
        catch (AgendaVersionConflictException) { return AgentToolResult.ConflictResult("O agendamento foi alterado. Confirme novamente."); }
        catch (AgendaLockUnavailableException) { return AgentToolResult.ConflictResult("A agenda está ocupada. Tente novamente."); }
    }

    private bool Authorized(AgentToolCaller caller) =>
        OperationConfirmationPolicy.IsAuthorized(caller, telegram.Value.AdministratorUserId);

    private static AgentToolResult Unauthorized() => AgentToolResult.Failed("Solicitação não autorizada.");

    private Task<SolicitacaoPendente?> FindAsync(long userId, long requestId, CancellationToken token) =>
        db.SolicitacoesPendentes.SingleOrDefaultAsync(item => item.Id == requestId && item.TelegramUserId == userId, token);

    private Task LockConversationAsync(long userId, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId}::text, 790));", token);

    private sealed class RejectedConfirmation(AgentToolResult result) : Exception
    {
        public AgentToolResult Result { get; } = result;
    }
}
