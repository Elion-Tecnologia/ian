using Ian.Application.AI;
using Ian.Domain.Clientes;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ian.Infrastructure.AI;

public interface IAgentToolRegistry
{
    IReadOnlyList<AIFunction> CreateTools(AgentToolCaller caller);
    Task BeginTurnAsync(AgentDialoguePlan plan, AgentToolCaller caller, CancellationToken ct);
}

public sealed class AgentToolRegistry(
    IAgentToolBackend backend,
    IOptions<TelegramOptions> telegram,
    AgentExecutionBudget budget,
    AgentTurnContext? turn = null,
    Microsoft.Extensions.Logging.ILogger<AgentToolRegistry>? logger = null) : IAgentToolRegistry
{
    public async Task BeginTurnAsync(AgentDialoguePlan plan, AgentToolCaller caller, CancellationToken ct)
    {
        if (!Ian.Application.Conversation.OperationConfirmationPolicy.IsAuthorized(caller, telegram.Value.AdministratorUserId))
            throw new InvalidOperationException("Solicitação não autorizada.");
        await backend.ExecuteAsync(new InterpretarTurnoRequest(plan), ct);
    }
    public IReadOnlyList<AIFunction> CreateTools(AgentToolCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return
        [
            AIFunctionFactory.Create((string? nome = null, string? telefone = null, string? enderecoPrincipal = null, CancellationToken ct = default) =>
                InvokeAsync(new CadastrarClienteToolRequest(nome, telefone, enderecoPrincipal), caller, ct),
                "cadastrar_cliente", "Cadastra um cliente por nome e telefone com DDD. Argumentos parciais são persistidos e os dados ausentes são solicitados."),
            AIFunctionFactory.Create((string? nome = null, string? telefone = null, CancellationToken ct = default) =>
                InvokeAsync(new LocalizarClienteToolRequest(nome, telefone), caller, ct),
                "localizar_cliente", "Localiza clientes por nome ou telefone, sem selecionar automaticamente entre vários IDs."),
            AIFunctionFactory.Create((CancellationToken ct = default) => InvokeAsync(new ListarServicosToolRequest(), caller, ct),
                "listar_servicos", "Lista IDs e padrões reais dos serviços disponíveis. Nunca invente um ID."),
            AIFunctionFactory.Create((DateOnly? dataInicial = null, DateOnly? dataFinal = null, long? clienteId = null, long? agendamentoId = null, CancellationToken ct = default) =>
                InvokeAsync(new ConsultarAgendaToolRequest(dataInicial, dataFinal, clienteId, agendamentoId), caller, ct),
                "consultar_agenda", "Consulta programação por dia/período ou ID de cliente/agendamento; dados reais e versão são validados pelo backend."),
            AIFunctionFactory.Create((DateOnly? data = null, TimeOnly? horario = null, int? duracao = null, string? unidadeDuracao = null, CancellationToken ct = default) =>
                InvokeAsync(new VerificarDisponibilidadeToolRequest(data, horario, duracao, unidadeDuracao), caller, ct),
                "verificar_disponibilidade", "Verifica ocupações reais. Unidade: Minutos ou DiasCorridos. DiasCorridos exige 08h–18h em cada data."),
            AIFunctionFactory.Create((string? nomeCliente = null, string? telefoneCliente = null, long? tipoServicoId = null, DateOnly? data = null, TimeOnly? horario = null, string? modalidade = null, int? duracao = null, string? unidadeDuracao = null, string? enderecoServico = null, string? observacoes = null, long? clienteId = null, CancellationToken ct = default) =>
                InvokeAsync(new CriarAgendamentoToolRequest(nomeCliente, telefoneCliente, tipoServicoId, data, horario, modalidade, duracao, unidadeDuracao, enderecoServico, observacoes, clienteId), caller, ct),
                "criar_agendamento", "Inicia ou continua qualquer pedido de agendar/reservar um serviço, mesmo sem nenhum dado. Chame com argumentos vazios para iniciar a coleta; não peça escolha entre operações. Padrões de duração, unidade e modalidade vêm do serviço. Modalidade: Interno, Externo ou Remoto. Unidade: Minutos ou DiasCorridos. Endereço obrigatório para Externo. Não corrija início divergente em DiasCorridos sem esclarecer com a administradora."),
            AIFunctionFactory.Create((long? agendamentoId = null, string? operacao = null, DateOnly? data = null, TimeOnly? horario = null, string? observacoes = null, long? versaoEsperada = null, long? tipoServicoId = null, string? modalidade = null, int? duracao = null, string? unidadeDuracao = null, string? enderecoServico = null, bool? alterarEndereco = null, bool? alterarObservacoes = null, long? clienteId = null, DateOnly? dataBusca = null, CancellationToken ct = default) =>
                InvokeAsync(new AlterarAgendamentoToolRequest(agendamentoId, operacao, data, horario, observacoes, versaoEsperada, tipoServicoId, modalidade, duracao, unidadeDuracao, enderecoServico, alterarEndereco, alterarObservacoes, clienteId, dataBusca), caller, ct),
                "alterar_agendamento", "Operações: editar, reagendar, concluir, cancelar. Reutiliza versão e seleção explícita. Para buscar alvo sem ID, informe clienteId ou dataBusca. Cancelar apenas prepara confirmação: a aceitação é feita por texto com código, fora do modelo. Flags alterarEndereco/alterarObservacoes permitem limpar valores explicitamente."),
            AIFunctionFactory.Create((CancellationToken ct = default) => InvokeAsync(new EsclarecerPedidoToolRequest(), caller, ct),
                "esclarecer_pedido", "Pede esclarecimento sem alterar dados, somente quando a intenção não pode ser identificada ou para saudações. Pedido de agendar, mesmo incompleto, usa criar_agendamento.")
        ];
    }

    private async Task<AgentToolResult> InvokeAsync(AgentToolRequest request, AgentToolCaller caller, CancellationToken cancellationToken)
    {
        if (logger is not null) Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
            "Update {UpdateId} stage=tool_started tool={Tool}.", turn?.UpdateId, request.GetType().Name);
        AgentToolResult Record(AgentToolResult result)
        {
            if (logger is not null) Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
                "Update {UpdateId} stage=tool_completed tool={Tool} outcome={Outcome} category={Category}.", turn?.UpdateId, request.GetType().Name, result.Outcome, result.FailureCategory);
            if (turn is not null)
            {
                turn.LastResult = result;
                if (request is CadastrarClienteToolRequest or CriarAgendamentoToolRequest or AlterarAgendamentoToolRequest || result.Outcome == AgentToolOutcome.Ambiguous)
                    turn.PendingResult = result;
            }
            return result;
        }
        if (!Ian.Application.Conversation.OperationConfirmationPolicy.IsAuthorized(caller, telegram.Value.AdministratorUserId))
            return Record(AgentToolResult.Failed("Solicitação não autorizada."));
        if (!budget.TryStartToolCall()) return Record(AgentToolResult.Failed("Limite de chamadas de ferramentas atingido.", AgentFailureCategory.ToolLimit));
        if (Invalid(request)) return Record(AgentToolResult.Failed("Dados inválidos. Use IDs positivos, telefone com DDD e valores dentro dos limites.", AgentFailureCategory.InvalidArguments));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(AgentExecutionLimits.ToolTimeoutSeconds));
        try
        {
            var result = await backend.ExecuteAsync(request, timeout.Token);
            if (result is null) return Record(AgentToolResult.Failed("A operação não retornou resultado válido.", AgentFailureCategory.ToolFailure));
            if (JsonSerializer.Serialize(result).Length > AgentExecutionLimits.MaxToolResultCharacters)
                return Record(AgentToolResult.Failed("O resultado excedeu o limite permitido. Refine a consulta.", AgentFailureCategory.ToolFailure));
            if (result.Outcome == AgentToolOutcome.Error && result.FailureCategory == AgentFailureCategory.Unknown)
                result = result with { FailureCategory = AgentFailureCategory.ToolFailure };
            return Record(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("Update {UpdateId} stage=tool_failed category=Timeout tool={Tool}.", turn?.UpdateId, request.GetType().Name);
            return Record(AgentToolResult.Failed("A operação excedeu o tempo limite.", AgentFailureCategory.ToolTimeout));
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Update {UpdateId} stage=tool_failed category=BackendFailure tool={Tool} exception_type={ExceptionType}.",
                turn?.UpdateId, request.GetType().Name, ex.GetType().Name);
            return Record(AgentToolResult.Failed("Não foi possível concluir a operação.", AgentFailureCategory.ToolFailure));
        }
    }

    private static bool Invalid(AgentToolRequest request) => request switch
    {
        CadastrarClienteToolRequest x => Long(x.Nome, 200) || Long(x.EnderecoPrincipal, 2000) || InvalidPhone(x.Telefone),
        LocalizarClienteToolRequest x => Long(x.Nome, 200) || InvalidPhone(x.Telefone),
        ConsultarAgendaToolRequest x => x.ClienteId is <= 0 || x.AgendamentoId is <= 0 || x.DataFinal < x.DataInicial ||
            x.DataInicial is { } a && x.DataFinal is { } b && b.DayNumber - a.DayNumber > 31,
        CriarAgendamentoToolRequest x => x.TipoServicoId is <= 0 || x.ClienteId is <= 0 || x.Duracao is <= 0 or > 720 ||
            Long(x.NomeCliente, 200) || InvalidPhone(x.TelefoneCliente) || Long(x.EnderecoServico, 2000) || Long(x.Observacoes, 2000),
        AlterarAgendamentoToolRequest x => x.AgendamentoId is <= 0 || x.VersaoEsperada is <= 0 || x.ClienteId is <= 0 ||
            x.TipoServicoId is <= 0 || x.Duracao is <= 0 or > 720 || Long(x.EnderecoServico, 2000) || Long(x.Observacoes, 2000),
        VerificarDisponibilidadeToolRequest x => x.Duracao is <= 0 or > 720,
        _ => false
    };
    private static bool Long(string? value, int maximum) => value?.Length > maximum;
    private static bool InvalidPhone(string? phone) => !string.IsNullOrWhiteSpace(phone) &&
        (phone.Length > 32 || !NormalizadorTelefoneBrasileiro.Normalizar(phone).Valido);
}
