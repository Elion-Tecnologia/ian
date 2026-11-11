using System.Globalization;
using System.Text.Json;
using Ian.Application.AI;
using Ian.Domain.Entities;

namespace Ian.Application.Conversation;

public sealed record ConfirmationChallenge(
    long PendingRequestId, Guid ChallengeId, long AgendamentoId, long Version, DateTimeOffset ExpiresAtUtc);

/// <summary>Explicit acceptance associated with the exact challenge by trusted text orchestration, never by the model.</summary>
public sealed record ConfirmationAcceptance(
    long PendingRequestId, Guid ChallengeId, long Version, AlterarAgendamentoToolRequest Arguments);

public sealed record StoredConfirmation(
    Guid ChallengeId, long AgendamentoId, long Version,
    AlterarAgendamentoToolRequest Arguments, DateTimeOffset ExpiresAtUtc);

public sealed record ConfirmedAgendaOperation(long AgendamentoId, long Version, AlterarAgendamentoToolRequest Arguments);

public interface IOperationConfirmation
{
    Task<AgentToolResult> PrepareAsync(
        AgentToolCaller caller, long pendingRequestId, AlterarAgendamentoToolRequest arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the authorized use case and consumes its challenge in one agenda transaction.
    /// The callback must revalidate business rules/availability and stage writes using the scoped context;
    /// it must not commit or start a nested transaction. Non-success results roll back all writes.
    /// </summary>
    Task<AgentToolResult> ExecuteAsync(
        AgentToolCaller caller, long updateId, ConfirmationAcceptance acceptance,
        Func<ConfirmedAgendaOperation, CancellationToken, Task<AgentToolResult>> mutation,
        CancellationToken cancellationToken = default);
}

public static class OperationConfirmationPolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsAuthorized(AgentToolCaller caller, long administratorId) =>
        administratorId > 0 && caller.UserId == administratorId && caller.ChatId == administratorId &&
        caller.IsPrivateChat && caller.IsHuman && caller.HasText;

    public static bool RequiresConfirmation(AlterarAgendamentoToolRequest arguments) =>
        string.Equals(arguments.Operacao, "cancelar", StringComparison.OrdinalIgnoreCase);

    public static AlterarAgendamentoToolRequest Normalize(AlterarAgendamentoToolRequest arguments) =>
        arguments with { Operacao = arguments.Operacao?.ToLowerInvariant() };

    public static AgentToolResult? ValidatePending(
        SolicitacaoPendente? pending, long userId, AlterarAgendamentoToolRequest arguments, DateTimeOffset now)
    {
        if (pending is null || pending.TelegramUserId != userId ||
            pending.Estado != EstadoSolicitacaoPendente.Pendente || pending.ExpiraEmUtc <= now ||
            pending.Operacao != nameof(PendingIntent.AlterarAgendamento))
            return AgentToolResult.ConflictResult("Solicitação ausente, encerrada ou expirada. Refaça o pedido.");

        var candidates = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(pending.CandidatosJson, JsonOptions)!;
        if (candidates.Count > 0)
            return AgentToolResult.Ambiguity("Selecione explicitamente um dos IDs candidatos antes de confirmar.", candidates);
        var missing = JsonSerializer.Deserialize<List<string>>(pending.CamposFaltantesJson, JsonOptions)!;
        if (missing.Count > 0) return AgentToolResult.Missing("Complete os dados pendentes antes de confirmar.");

        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(pending.DadosColetadosJson, JsonOptions)!;
        if (arguments.AgendamentoId is not > 0 || !RequiresConfirmation(arguments) ||
            arguments.Data is not null || arguments.Horario is not null || arguments.Observacoes is not null ||
            !fields.TryGetValue("agendamentoId", out var target) ||
            target != arguments.AgendamentoId.Value.ToString(CultureInfo.InvariantCulture) ||
            !fields.TryGetValue("operacao", out var operation) ||
            !string.Equals(operation, arguments.Operacao, StringComparison.OrdinalIgnoreCase))
            return AgentToolResult.ConflictResult("O alvo ou os argumentos divergem da solicitação. Refaça o pedido.");
        return null;
    }

    public static AgentToolResult? ValidateAcceptance(
        StoredConfirmation? stored, ConfirmationAcceptance acceptance, Agendamento? target, DateTimeOffset now)
    {
        if (stored is null || stored.ChallengeId != acceptance.ChallengeId ||
            stored.ExpiresAtUtc <= now || stored.Version != acceptance.Version ||
            stored.Arguments != Normalize(acceptance.Arguments) ||
            target is null || target.Id != stored.AgendamentoId || target.Versao != stored.Version ||
            target.Estado != EstadoAgendamento.Agendado)
            return AgentToolResult.ConflictResult("Confirmação inválida, expirada ou obsoleta. Releia o agendamento e confirme novamente.");
        return null;
    }
}
