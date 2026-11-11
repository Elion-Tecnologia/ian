using Ian.Domain.Entities;

namespace Ian.Application.Conversation;

public enum PendingIntent
{
    CadastrarCliente,
    CriarAgendamento,
    AlterarAgendamento
}

public enum PendingIntentStartStatus
{
    Started,
    ReplacementConfirmationRequired,
    Invalid
}

public enum PendingIntentUpdateStatus
{
    Updated,
    NotFound,
    Expired,
    Cancelled,
    InvalidCandidate
}

public sealed record PendingConversationState(
    long Id,
    long TelegramUserId,
    PendingIntent Intent,
    IReadOnlyDictionary<string, string> CollectedFields,
    IReadOnlyList<string> MissingFields,
    IReadOnlyDictionary<string, IReadOnlyList<long>> CandidateIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record PendingIntentStartResult(
    PendingIntentStartStatus Status,
    PendingConversationState? State);

public sealed record PendingIntentRepositoryStartResult(
    PendingIntentStartStatus Status,
    SolicitacaoPendente? State);

public interface IPendingConversationRepository
{
    Task<PendingIntentRepositoryStartResult> StartAsync(
        SolicitacaoPendente request,
        bool replacementConfirmed,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<SolicitacaoPendente?> GetActiveAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PendingIntentUpdateStatus> UpdateAsync(
        long telegramUserId,
        long requestId,
        string collectedFieldsJson,
        string missingFieldsJson,
        string candidateIdsJson,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PendingIntentUpdateStatus> SelectCandidateAsync(
        long telegramUserId,
        long requestId,
        string field,
        long candidateId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PendingIntentUpdateStatus> CancelAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public interface IPendingConversation
{
    Task<PendingIntentStartResult> StartAsync(
        long telegramUserId,
        PendingIntent intent,
        IReadOnlyDictionary<string, string>? collectedFields,
        IReadOnlyList<string>? missingFields,
        bool replacementConfirmed,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PendingConversationState?> GetForTurnAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PendingIntentUpdateStatus> UpdateAsync(
        long telegramUserId,
        IReadOnlyDictionary<string, string> collectedFields,
        IReadOnlyList<string> missingFields,
        IReadOnlyDictionary<string, IReadOnlyList<long>>? candidateIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PendingIntentUpdateStatus> SelectCandidateAsync(
        long telegramUserId,
        string field,
        long candidateId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<PendingIntentUpdateStatus> CancelAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
