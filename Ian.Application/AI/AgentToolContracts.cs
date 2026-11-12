using System.Text.Json.Serialization;

namespace Ian.Application.AI;

public enum AgentToolOutcome
{
    Success,
    MissingData,
    Ambiguous,
    Conflict,
    ConfirmationRequired,
    Error
}

public sealed record AgentToolResult
{
    private AgentToolResult(AgentToolOutcome outcome, string message, object? data = null)
    {
        Outcome = outcome;
        Message = message;
        Data = data;
    }

    [JsonIgnore]
    public AgentToolOutcome Outcome { get; }
    public string Status => Outcome.ToString();
    public string Message { get; init; }
    public object? Data { get; init; }

    public static AgentToolResult Succeeded(string message, object? data = null) =>
        new(AgentToolOutcome.Success, message, data);

    public static AgentToolResult Missing(string message) => new(AgentToolOutcome.MissingData, message);
    public static AgentToolResult Ambiguity(string message, object? data = null) => new(AgentToolOutcome.Ambiguous, message, data);
    public static AgentToolResult ConflictResult(string message) => new(AgentToolOutcome.Conflict, message);
    public static AgentToolResult RequiresConfirmation(string message, object? data = null) => new(AgentToolOutcome.ConfirmationRequired, message, data);
    public static AgentToolResult Failed(string message) => new(AgentToolOutcome.Error, message);
}

/// <summary>Trusted Telegram metadata supplied by the application host, never by the model.</summary>
public sealed record AgentToolCaller(long UserId, long ChatId, bool IsPrivateChat, bool IsHuman, bool HasText);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CadastrarClienteToolRequest), "cadastrar")]
[JsonDerivedType(typeof(LocalizarClienteToolRequest), "localizar")]
[JsonDerivedType(typeof(ListarServicosToolRequest), "servicos")]
[JsonDerivedType(typeof(EsclarecerPedidoToolRequest), "esclarecer")]
[JsonDerivedType(typeof(ConsultarAgendaToolRequest), "consultar")]
[JsonDerivedType(typeof(VerificarDisponibilidadeToolRequest), "disponibilidade")]
[JsonDerivedType(typeof(CriarAgendamentoToolRequest), "criar")]
[JsonDerivedType(typeof(AlterarAgendamentoToolRequest), "alterar")]
[JsonDerivedType(typeof(ConfirmarCancelamentoCommand), "confirmar")]
public abstract record AgentToolRequest;
public sealed record LocalizarClienteToolRequest(string? Nome = null, string? Telefone = null) : AgentToolRequest;
public sealed record ListarServicosToolRequest : AgentToolRequest;
public sealed record EsclarecerPedidoToolRequest : AgentToolRequest;
// Trusted text orchestration only. This command is never registered as a model tool.
public sealed record ConfirmarCancelamentoCommand(Ian.Application.Conversation.ConfirmationAcceptance Acceptance) : AgentToolRequest;
public sealed record CadastrarClienteToolRequest(string? Nome, string? Telefone, string? EnderecoPrincipal) : AgentToolRequest;
public sealed record ConsultarAgendaToolRequest(DateOnly? DataInicial, DateOnly? DataFinal, long? ClienteId, long? AgendamentoId) : AgentToolRequest;
public sealed record VerificarDisponibilidadeToolRequest(DateOnly? Data, TimeOnly? Horario, int? Duracao, string? UnidadeDuracao) : AgentToolRequest;
public sealed record CriarAgendamentoToolRequest(string? NomeCliente, string? TelefoneCliente, long? TipoServicoId, DateOnly? Data, TimeOnly? Horario, string? Modalidade, int? Duracao, string? UnidadeDuracao, string? EnderecoServico = null, string? Observacoes = null, long? ClienteId = null) : AgentToolRequest;
public sealed record AlterarAgendamentoToolRequest(long? AgendamentoId, string? Operacao, DateOnly? Data, TimeOnly? Horario, string? Observacoes, long? VersaoEsperada = null, long? TipoServicoId = null, string? Modalidade = null, int? Duracao = null, string? UnidadeDuracao = null, string? EnderecoServico = null, bool? AlterarEndereco = null, bool? AlterarObservacoes = null, long? ClienteId = null, DateOnly? DataBusca = null) : AgentToolRequest;

public interface IAgentToolBackend
{
    Task<AgentToolResult> ExecuteAsync(AgentToolRequest request, CancellationToken cancellationToken = default);
}

public sealed class AgentExecutionLimits
{
    public const int MaxTurns = 6;
    public const int MaxToolCalls = 8;
    public const int MaxPromptCharacters = 16_000;
    public const int MaxToolResultCharacters = 8_000;
    public const int TimeoutSeconds = 60;
    public const int ToolTimeoutSeconds = 15;
}

public sealed class AgentExecutionBudget
{
    private int _turns;
    private int _toolCalls;

    public bool TryStartTurn() => Interlocked.Increment(ref _turns) <= AgentExecutionLimits.MaxTurns;
    public bool TryStartToolCall() => Interlocked.Increment(ref _toolCalls) <= AgentExecutionLimits.MaxToolCalls;
}

public static class AgentToolPolicy
{
    public const string SystemInstructions = "Você é o assistente textual da agenda. Interprete a intenção em português natural e use a ferramenta correspondente mesmo com dados parciais ou sem argumentos. Um pedido de agendar inicia criar_agendamento; não exija palavras exatas nem que a pessoa escolha entre operações. Cadastro e localização de cliente são etapas intermediárias quando há um agendamento pendente. Preserve esse objetivo e use somente os novos dados informados. Se não houver intenção identificável, use esclarecer_pedido, sem inventar uma operação. O backend decide validação, disponibilidade, autorização e confirmação. Nunca afirme que uma operação ocorreu sem resultado Success. Em MissingData ou Ambiguous, peça esclarecimento; em Conflict, explique que não foi concluída; em ConfirmationRequired, peça confirmação explícita; em Error, informe que não foi possível concluir. Não invente dados nem exponha detalhes internos.";
}

public static class AgentToolResultTranslator
{
    public static string ToDialogue(AgentToolResult? result)
    {
        if (result is null) return "Não foi possível concluir a solicitação.";
        if (result.Outcome == AgentToolOutcome.Error)
            return "Não foi possível concluir a solicitação.";
        var message = string.IsNullOrWhiteSpace(result.Message)
            ? "Não foi possível concluir a solicitação."
            : result.Message.Trim();
        if (message.Length > AgentExecutionLimits.MaxToolResultCharacters)
            message = message[..AgentExecutionLimits.MaxToolResultCharacters];
        return message;
    }
}
