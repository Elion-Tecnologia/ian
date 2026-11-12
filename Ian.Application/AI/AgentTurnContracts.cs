using System.Text.Json;
using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Application.AI;

public interface IAgendaAgentRunner
{
    Task<AiModelResult> RunAsync(string prompt, AgentToolCaller caller, CancellationToken cancellationToken = default);
}

// Trusted host metadata. None of these values is accepted as a function argument from the model.
public sealed class AgentTurnContext
{
    public long UpdateId { get; private set; } = -1;
    public AgentToolCaller? Caller { get; private set; }
    public string Text { get; private set; } = "";
    public bool CatalogWasShown { get; set; }
    public bool SelectionRequired { get; set; }
    public bool WriteAttempted { get; set; }
    public AgentToolResult? PendingResult { get; set; }
    public AgentToolResult? LastResult { get; set; }
    public AgentToolResult? MutationResult { get; set; }
    public AgentToolResult? ClarificationResult { get; set; }
    public AgentToolResult? CatalogResult { get; set; }
    public void Start(long updateId, AgentToolCaller caller, string text)
    {
        UpdateId = updateId;
        Caller = caller;
        Text = text;
        CatalogWasShown = false;
        SelectionRequired = false;
        WriteAttempted = false;
        PendingResult = null;
        LastResult = null;
        MutationResult = null;
        ClarificationResult = null;
        CatalogResult = null;
    }
}

public interface IAgentTurnStore
{
    Task<IAsyncDisposable> LockAsync(long userId, CancellationToken cancellationToken);
    Task<TelegramAgentTurn> GetOrCreateAsync(long updateId, long userId, CancellationToken cancellationToken);
    Task<bool> FreezeCommandAsync(long updateId, string commandJson, CancellationToken cancellationToken);
    Task<long?> GetCommittedAppointmentAsync(long updateId, CancellationToken cancellationToken);
    Task<StoredConfirmation?> GetConfirmationAsync(long userId, long pendingId, CancellationToken cancellationToken);
    Task<string?> GetPreviousResponseAsync(long userId, long updateId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentConversationTurn>> GetHistoryAsync(long userId, long updateId, CancellationToken cancellationToken);
    Task CompletePendingAsync(long userId, CancellationToken cancellationToken);
    Task SaveResponseAsync(long updateId, string response, CancellationToken cancellationToken);
}

public static class AgentRequestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
    public static string Serialize(AgentToolRequest request) => JsonSerializer.Serialize(request, Options);
    public static AgentToolRequest Deserialize(string json) => JsonSerializer.Deserialize<AgentToolRequest>(json, Options)
        ?? throw new JsonException("Solicitação inválida.");
}
