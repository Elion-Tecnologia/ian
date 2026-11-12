using System.Text.Json;
using Ian.Domain.Entities;

namespace Ian.Application.Conversation;

public sealed class PendingConversationService(IPendingConversationRepository repository) : IPendingConversation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private const int MaxSerializedStateLength = 12_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PendingIntentStartResult> StartAsync(
        long telegramUserId,
        PendingIntent intent,
        IReadOnlyDictionary<string, string>? collectedFields,
        IReadOnlyList<string>? missingFields,
        bool replacementConfirmed,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0 || !Enum.IsDefined(intent))
            return new(PendingIntentStartStatus.Invalid, null);

        var collected = collectedFields is null ? new Dictionary<string, string>() : new Dictionary<string, string>(collectedFields);
        var missing = missingFields?.Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (!IsValidCollected(collected) || !IsValidNames(missing))
            return new(PendingIntentStartStatus.Invalid, null);

        var created = nowUtc.ToUniversalTime();
        var request = new SolicitacaoPendente
        {
            TelegramUserId = telegramUserId,
            Operacao = intent.ToString(),
            DadosColetadosJson = JsonSerializer.Serialize(collected, JsonOptions),
            CamposFaltantesJson = JsonSerializer.Serialize(missing, JsonOptions),
            CandidatosJson = "{}",
            CriadaEmUtc = created,
            AtualizadaEmUtc = created,
            ExpiraEmUtc = created.Add(Lifetime)
        };

        var result = await repository.StartAsync(request, replacementConfirmed, created, cancellationToken);
        return new(result.Status, result.State is null ? null : ToState(result.State));
    }

    public async Task<PendingConversationState?> GetForTurnAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0) return null;
        var request = await repository.GetActiveAsync(telegramUserId, nowUtc.ToUniversalTime(), cancellationToken);
        return request is null ? null : ToState(request);
    }

    public async Task<PendingIntentUpdateStatus> UpdateAsync(
        long telegramUserId,
        IReadOnlyDictionary<string, string> collectedFields,
        IReadOnlyList<string> missingFields,
        IReadOnlyDictionary<string, IReadOnlyList<long>>? candidateIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0 || !IsValidCollected(collectedFields) || !IsValidNames(missingFields))
            return PendingIntentUpdateStatus.InvalidCandidate;
        var current = await repository.GetActiveAsync(telegramUserId, nowUtc.ToUniversalTime(), cancellationToken);
        if (current is null) return PendingIntentUpdateStatus.NotFound;

        var mergedFields = JsonSerializer.Deserialize<Dictionary<string, string>>(current.DadosColetadosJson, JsonOptions) ?? [];
        foreach (var pair in collectedFields) mergedFields[pair.Key] = pair.Value;
        var mergedCandidates = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(current.CandidatosJson, JsonOptions) ?? [];
        if (candidateIds is not null)
            foreach (var pair in candidateIds)
                mergedCandidates[pair.Key] = pair.Value.Distinct().ToList();
        var candidates = mergedCandidates.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<long>)pair.Value);
        var distinctMissing = missingFields.Distinct(StringComparer.Ordinal).ToList();
        if (!IsValidCollected(mergedFields) || !IsValidCandidates(candidates) ||
            SerializedLength(mergedFields) + SerializedLength(distinctMissing) + SerializedLength(candidates) > MaxSerializedStateLength)
            return PendingIntentUpdateStatus.InvalidCandidate;

        return await repository.UpdateAsync(
            telegramUserId,
            current.Id,
            JsonSerializer.Serialize(mergedFields, JsonOptions),
            JsonSerializer.Serialize(distinctMissing, JsonOptions),
            JsonSerializer.Serialize(candidates, JsonOptions),
            nowUtc.ToUniversalTime(),
            cancellationToken);
    }

    public async Task<PendingIntentUpdateStatus> SelectCandidateAsync(
        long telegramUserId,
        string field,
        long candidateId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0 || !ValidName(field) || candidateId <= 0)
            return PendingIntentUpdateStatus.InvalidCandidate;
        var current = await repository.GetActiveAsync(telegramUserId, nowUtc.ToUniversalTime(), cancellationToken);
        if (current is null) return PendingIntentUpdateStatus.NotFound;
        return await repository.SelectCandidateAsync(
            telegramUserId, current.Id, field, candidateId, nowUtc.ToUniversalTime(), cancellationToken);
    }

    public Task<PendingIntentUpdateStatus> CancelAsync(
        long telegramUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        telegramUserId <= 0
            ? Task.FromResult(PendingIntentUpdateStatus.NotFound)
            : repository.CancelAsync(telegramUserId, nowUtc.ToUniversalTime(), cancellationToken);

    private static PendingConversationState ToState(SolicitacaoPendente request)
    {
        var intent = Enum.Parse<PendingIntent>(request.Operacao, ignoreCase: false);
        var collected = JsonSerializer.Deserialize<Dictionary<string, string>>(request.DadosColetadosJson, JsonOptions) ?? [];
        var missing = JsonSerializer.Deserialize<List<string>>(request.CamposFaltantesJson, JsonOptions) ?? [];
        var candidates = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(request.CandidatosJson, JsonOptions) ?? [];
        return new(request.Id, request.TelegramUserId, intent, collected,
            missing, candidates.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<long>)pair.Value),
            request.CriadaEmUtc, request.AtualizadaEmUtc, request.ExpiraEmUtc);
    }

    private static bool IsValidCollected(IReadOnlyDictionary<string, string> fields) =>
        fields.Count <= 32 && fields.All(pair => ValidName(pair.Key) && pair.Value is not null && pair.Value.Length <= (pair.Key == "request" ? 8_000 : 2_000)) &&
        SerializedLength(fields) <= MaxSerializedStateLength;

    private static bool IsValidNames(IEnumerable<string> names) =>
        names.Count() <= 32 && names.All(ValidName);

    private static bool IsValidCandidates(IReadOnlyDictionary<string, IReadOnlyList<long>> candidates) =>
        candidates.Count <= 16 && candidates.All(pair => ValidName(pair.Key) && pair.Value.Count is > 0 and <= 20 && pair.Value.All(id => id > 0)) &&
        SerializedLength(candidates) <= MaxSerializedStateLength;

    private static bool ValidName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static int SerializedLength<T>(T value) => JsonSerializer.Serialize(value, JsonOptions).Length;
}
