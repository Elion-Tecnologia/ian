using Ian.Application.AI;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ian.Domain.Clientes;

namespace Ian.Infrastructure.AI;

public interface IAgentToolRegistry
{
    IReadOnlyList<AIFunction> CreateTools(AgentToolCaller caller);
}

public sealed class AgentToolRegistry(
    IAgentToolBackend backend,
    IOptions<TelegramOptions> telegram,
    AgentExecutionBudget budget) : IAgentToolRegistry
{
    public IReadOnlyList<AIFunction> CreateTools(AgentToolCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return
        [
            CreateFunction((string? nome = null, string? telefone = null, string? enderecoPrincipal = null, CancellationToken ct = default) =>
                InvokeAsync(new CadastrarClienteToolRequest(nome, telefone, enderecoPrincipal), caller, budget, ct),
                "cadastrar_cliente", "Solicita cadastro de cliente com nome, telefone e endereço opcional."),
            CreateFunction((DateOnly? dataInicial = null, DateOnly? dataFinal = null, long? clienteId = null, long? agendamentoId = null, CancellationToken ct = default) =>
                InvokeAsync(new ConsultarAgendaToolRequest(dataInicial, dataFinal, clienteId, agendamentoId), caller, budget, ct),
                "consultar_agenda", "Consulta agendamentos por período ou identificador de cliente/agendamento."),
            CreateFunction((DateOnly? data = null, TimeOnly? horario = null, int? duracao = null, string? unidadeDuracao = null, CancellationToken ct = default) =>
                InvokeAsync(new VerificarDisponibilidadeToolRequest(data, horario, duracao, unidadeDuracao), caller, budget, ct),
                "verificar_disponibilidade", "Verifica disponibilidade para uma data, horário e duração."),
            CreateFunction((string? nomeCliente = null, string? telefoneCliente = null, long? tipoServicoId = null, DateOnly? data = null, TimeOnly? horario = null, string? modalidade = null, int? duracao = null, string? unidadeDuracao = null, CancellationToken ct = default) =>
                InvokeAsync(new CriarAgendamentoToolRequest(nomeCliente, telefoneCliente, tipoServicoId, data, horario, modalidade, duracao, unidadeDuracao), caller, budget, ct),
                "criar_agendamento", "Solicita criação de agendamento com dados de cliente e serviço."),
            CreateFunction((long? agendamentoId = null, string? operacao = null, DateOnly? data = null, TimeOnly? horario = null, string? observacoes = null, CancellationToken ct = default) =>
                InvokeAsync(new AlterarAgendamentoToolRequest(agendamentoId, operacao, data, horario, observacoes), caller, budget, ct),
                "alterar_agendamento", "Solicita alteração de agendamento; operações sensíveis exigem confirmação do backend.")
        ];
    }

    private static AIFunction CreateFunction(Delegate function, string name, string description) =>
        AIFunctionFactory.Create(function, name, description, new JsonSerializerOptions(AIJsonUtilities.DefaultOptions)
        {
            Converters = { new JsonStringEnumConverter() }
        });

    private async Task<AgentToolResult> InvokeAsync(AgentToolRequest request, AgentToolCaller caller, AgentExecutionBudget budget, CancellationToken cancellationToken)
    {
        var expectedAdminId = telegram.Value.AdministratorUserId;
        if (expectedAdminId <= 0 || caller.UserId != expectedAdminId || caller.ChatId != expectedAdminId ||
            !caller.IsPrivateChat || !caller.IsHuman || !caller.HasText)
        {
            return AgentToolResult.Failed("Solicitação não autorizada.");
        }

        var missing = MissingFields(request);
        if (missing is not null) return AgentToolResult.Missing($"Informe: {missing}.");
        var invalid = InvalidFields(request);
        if (invalid is not null) return AgentToolResult.Failed($"Dados inválidos: {invalid}.");
        if (!budget.TryStartToolCall()) return AgentToolResult.Failed("Limite de chamadas de ferramentas atingido.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(AgentExecutionLimits.ToolTimeoutSeconds));
        try
        {
            var result = await backend.ExecuteAsync(request, timeout.Token);
            if (result is null) return AgentToolResult.Failed("A operação não retornou um resultado válido.");
            var bounded = result with { Message = Limit(result.Message) };
            return ResultFitsLimit(bounded)
                ? bounded
                : AgentToolResult.Failed("O resultado excedeu o limite permitido.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return AgentToolResult.Failed("A operação excedeu o tempo limite.");
        }
        catch (Exception)
        {
            return AgentToolResult.Failed("Não foi possível concluir a operação.");
        }
    }

    private static string? MissingFields(AgentToolRequest request) => request switch
    {
        CadastrarClienteToolRequest x when Blank(x.Nome) || Blank(x.Telefone) => Join(Blank(x.Nome) ? "nome" : null, Blank(x.Telefone) ? "telefone" : null),
        ConsultarAgendaToolRequest x when x.AgendamentoId is null && x.ClienteId is null && x.DataInicial is null => "data inicial, cliente ou agendamento",
        VerificarDisponibilidadeToolRequest x when x.Data is null || x.Duracao is null || Blank(x.UnidadeDuracao) => Join(x.Data is null ? "data" : null, x.Duracao is null ? "duração" : null, Blank(x.UnidadeDuracao) ? "unidade de duração" : null),
        CriarAgendamentoToolRequest x when Blank(x.NomeCliente) || Blank(x.TelefoneCliente) || x.TipoServicoId is null || x.Data is null || Blank(x.Modalidade) || x.Duracao is null || Blank(x.UnidadeDuracao) => Join(Blank(x.NomeCliente) ? "nome do cliente" : null, Blank(x.TelefoneCliente) ? "telefone do cliente" : null, x.TipoServicoId is null ? "serviço" : null, x.Data is null ? "data" : null, Blank(x.Modalidade) ? "modalidade" : null, x.Duracao is null ? "duração" : null, Blank(x.UnidadeDuracao) ? "unidade de duração" : null),
        AlterarAgendamentoToolRequest x when x.AgendamentoId is null || Blank(x.Operacao) => Join(x.AgendamentoId is null ? "agendamento" : null, Blank(x.Operacao) ? "operação" : null),
        _ => null
    };

    private static string? InvalidFields(AgentToolRequest request) => request switch
    {
        CadastrarClienteToolRequest x when x.Nome?.Length > 200 => "nome",
        CadastrarClienteToolRequest x when x.Telefone?.Length > 32 || !NormalizadorTelefoneBrasileiro.Normalizar(x.Telefone).Valido => "telefone",
        ConsultarAgendaToolRequest x when x.ClienteId is <= 0 || x.AgendamentoId is <= 0 => "identificador",
        ConsultarAgendaToolRequest x when x.DataFinal < x.DataInicial || x.DataInicial is { } start && x.DataFinal is { } end && end.DayNumber - start.DayNumber > 366 => "intervalo de datas",
        VerificarDisponibilidadeToolRequest x when x.Duracao is <= 0 or > 720 => "duração",
        VerificarDisponibilidadeToolRequest x when x.UnidadeDuracao is not null && x.UnidadeDuracao is not ("DiasCorridos" or "Horas") => "unidade de duração",
        CriarAgendamentoToolRequest x when x.TipoServicoId is <= 0 || x.Duracao is <= 0 or > 720 => "serviço ou duração",
        CriarAgendamentoToolRequest x when x.NomeCliente?.Length > 200 || x.TelefoneCliente?.Length > 32 || !NormalizadorTelefoneBrasileiro.Normalizar(x.TelefoneCliente).Valido => "dados do cliente",
        CriarAgendamentoToolRequest x when x.Modalidade is not null && x.Modalidade is not ("NaLoja" or "Externo") => "modalidade",
        CriarAgendamentoToolRequest x when x.UnidadeDuracao is not null && x.UnidadeDuracao is not ("DiasCorridos" or "Horas") => "unidade de duração",
        AlterarAgendamentoToolRequest x when x.AgendamentoId is <= 0 || x.Observacoes?.Length > 2000 => "identificador ou observações",
        _ => null
    };

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static string Join(params string?[] fields) => string.Join(", ", fields.Where(field => field is not null));
    private static string Limit(string? message) => string.IsNullOrWhiteSpace(message)
        ? "A operação foi concluída sem uma mensagem de resultado válida."
        : message.Length <= AgentExecutionLimits.MaxToolResultCharacters
            ? message
            : message[..AgentExecutionLimits.MaxToolResultCharacters];

    private static bool ResultFitsLimit(AgentToolResult result)
    {
        try
        {
            return JsonSerializer.Serialize(result).Length <= AgentExecutionLimits.MaxToolResultCharacters;
        }
        catch (JsonException)
        {
            return false;
        }
    }

}
