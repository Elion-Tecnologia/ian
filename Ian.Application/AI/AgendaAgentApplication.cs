using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ian.Application.Agenda;
using Ian.Application.Catalogo;
using Ian.Application.Clientes;
using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Application.AI;

public sealed class AgendaAgentApplication(
    AgentTurnContext turn,
    IPendingConversation conversations,
    IAgentTurnStore turns,
    IClientes clientes,
    ITipoServicoCatalogo catalogo,
    IConsultaAgenda consulta,
    IDisponibilidadeAgenda disponibilidade,
    ICriacaoAgendamento criacao,
    IEdicaoAgendamento edicao,
    IEncerramentoAgendamento encerramento,
    TimeProvider clock) : IAgentToolBackend
{
    public async Task<AgentToolResult> ExecuteAsync(AgentToolRequest request, CancellationToken cancellationToken = default)
    {
        var caller = turn.Caller;
        if (turn.UpdateId < 0 || caller is null || !caller.IsPrivateChat || !caller.IsHuman || !caller.HasText || caller.UserId != caller.ChatId)
            return AgentToolResult.Failed("Solicitação não autorizada.");
        cancellationToken.ThrowIfCancellationRequested();
        if (request is InterpretarTurnoRequest interpretation)
        {
            var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), cancellationToken);
            if (pending is null && interpretation.Plan.Objetivo == DialogueGoal.CriarAgendamento)
            {
                var initial = PendingTurnDataCapture.InitialSchedule(turn.Text, clock.GetUtcNow());
                turn.PendingResult = await PrepararAsync(initial, cancellationToken);
                pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), cancellationToken);
            }
            if (pending?.Intent == PendingIntent.CriarAgendamento && interpretation.Plan.Objetivo == DialogueGoal.CriarAgendamento &&
                Regex.IsMatch(turn.Text, @"\b(restante|resto)\b.{0,30}\b(dia|hoje)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                await conversations.UpdateAsync(UserId, new Dictionary<string, string> { ["periodoRestante"] = "true" },
                    pending.MissingFields, null, clock.GetUtcNow(), cancellationToken);
            return AgentToolResult.Succeeded("Objetivo recuperado para este turno.");
        }
        if (turn.SelectionRequired && request is CadastrarClienteToolRequest or CriarAgendamentoToolRequest or AlterarAgendamentoToolRequest)
            return AgentToolResult.Ambiguity("Selecione explicitamente um dos IDs apresentados antes de executar outra operação.");
        if (turn.MutationResult is { } completed) return completed;
        if (turn.ClarificationResult is { } clarification && request is CadastrarClienteToolRequest or CriarAgendamentoToolRequest or AlterarAgendamentoToolRequest)
            return clarification;
        if (turn.WriteAttempted && request is CadastrarClienteToolRequest or CriarAgendamentoToolRequest or AlterarAgendamentoToolRequest)
            return AgentToolResult.Failed("Já tentei executar este pedido. Consulte a agenda antes de tentar novamente.");
        if (request is CadastrarClienteToolRequest or LocalizarClienteToolRequest &&
            (await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), cancellationToken))?.Intent == PendingIntent.CriarAgendamento)
        {
            // Customer tools supply data to the existing goal. Creation still owns the single,
            // atomic write of customer + appointment; no independent customer write is performed.
            var dados = request is CadastrarClienteToolRequest cadastro
                ? new CriarAgendamentoToolRequest(cadastro.Nome, cadastro.Telefone, null, null, null, null, null, null)
                : new CriarAgendamentoToolRequest(((LocalizarClienteToolRequest)request).Nome,
                    ((LocalizarClienteToolRequest)request).Telefone, null, null, null, null, null, null);
            return turn.PendingResult = await ExecuteAsync(dados, cancellationToken);
        }
        return request switch
        {
            EsclarecerPedidoToolRequest => await EsclarecerAsync(cancellationToken),
            ListarServicosToolRequest => await ServicosAsync(cancellationToken),
            LocalizarClienteToolRequest r => await LocalizarAsync(r, cancellationToken),
            ConsultarAgendaToolRequest r => await ConsultarAsync(r, cancellationToken),
            VerificarDisponibilidadeToolRequest r => await DisponibilidadeAsync(r, cancellationToken),
            CadastrarClienteToolRequest or CriarAgendamentoToolRequest or AlterarAgendamentoToolRequest => await PrepararAsync(request, cancellationToken),
            _ => AgentToolResult.Failed("Operação não disponível.")
        };
    }

    public async Task<AgentToolResult> ResumeAsync(string commandJson, CancellationToken cancellationToken)
    {
        if (await turns.GetCommittedAppointmentAsync(turn.UpdateId, cancellationToken) is { } id)
        {
            await turns.CompletePendingAsync(UserId, cancellationToken);
            return turn.MutationResult = await ResumoAsync(id, "Seu pedido já foi salvo. Dados atuais", cancellationToken);
        }
        return await PersistirAsync(AgentRequestJson.Deserialize(commandJson), cancellationToken);
    }

    public async Task<AgentToolResult> ConfirmarAsync(ConfirmationAcceptance acceptance, CancellationToken cancellationToken) =>
        await PersistirAsync(new ConfirmarCancelamentoCommand(acceptance), cancellationToken);

    public async Task<AgentToolResult> RetomarSelecaoAsync(CancellationToken cancellationToken)
    {
        var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), cancellationToken);
        if (pending?.CollectedFields.GetValueOrDefault("request") is not { } json)
            return AgentToolResult.ConflictResult("Refaça o pedido com o ID selecionado.");
        if (pending.Intent == PendingIntent.LocalizarCliente && pending.CollectedFields.TryGetValue("clienteId", out var selected) && long.TryParse(selected, out var selectedId))
        {
            var found = await clientes.LocalizarPorIdAsync(selectedId, cancellationToken);
            if (found.Estado != EstadoLocalizacaoCliente.Unico) return AgentToolResult.ConflictResult("Cliente não encontrado. Refaça a busca.");
            await turns.CompletePendingAsync(UserId, cancellationToken);
            var c = found.Clientes[0];
            return AgentToolResult.Succeeded($"Cliente selecionado: {c.Nome}, telefone {c.TelefoneNormalizado}.", c);
        }
        var node = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("Solicitação ausente.");
        foreach (var field in new[] { "clienteId", "agendamentoId" })
            if (pending.CollectedFields.TryGetValue(field, out var value) && long.TryParse(value, out var id)) node[field] = id;
        return await PrepararAsync(AgentRequestJson.Deserialize(node.ToJsonString()), cancellationToken);
    }

    public async Task<AgentToolResult?> CapturePendingReplyAsync(CancellationToken ct)
    {
        var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
        if (pending is null) return null;
        if (PendingTurnDataCapture.Capture(turn.Text, pending) is { } request)
            return await ExecuteAsync(request, ct);
        if (PendingTurnDataCapture.IsInvalidPhoneReply(turn.Text, pending))
            return AgentToolResult.Missing("Esse telefone não parece válido. Pode informar um telefone brasileiro com DDD?") with
                { DialogueMode = AgentDialogueMode.Collection, QuestionTopic = "telefone", FailureCategory = AgentFailureCategory.InvalidArguments };
        return null;
    }

    private long UserId => turn.Caller?.UserId ?? throw new InvalidOperationException("Turno não autorizado.");

    private async Task<AgentToolResult> PrepararAsync(AgentToolRequest request, CancellationToken ct)
    {
        var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
        if (pending?.CollectedFields.GetValueOrDefault("executionUpdateId") is { } previousUpdate &&
            long.TryParse(previousUpdate, out var previousId) && previousId != turn.UpdateId &&
            await turns.GetCommittedAppointmentAsync(previousId, ct) is { } persistedId)
        {
            await turns.CompletePendingAsync(UserId, ct);
            return turn.MutationResult = await ResumoAsync(persistedId, "Seu pedido já foi salvo. Dados atuais", ct);
        }
        if (pending?.CandidateIds.Count > 0)
            return AgentToolResult.Ambiguity("Selecione explicitamente um ID: envie cliente <ID> ou agendamento <ID>.", pending.CandidateIds);
        var intent = request switch
        {
            CadastrarClienteToolRequest => PendingIntent.CadastrarCliente,
            CriarAgendamentoToolRequest => PendingIntent.CriarAgendamento,
            _ => PendingIntent.AlterarAgendamento
        };
        if (pending is not null && pending.Intent != intent)
            return AgentToolResult.ConflictResult("Há outro pedido pendente. Para descartá-lo explicitamente envie /descartar e então faça o novo pedido.");
        if (pending?.CollectedFields.GetValueOrDefault("request") is { } old)
        {
            var merged = JsonNode.Parse(old)?.AsObject() ?? throw new InvalidOperationException();
            var incoming = JsonNode.Parse(AgentRequestJson.Serialize(request))?.AsObject() ?? throw new InvalidOperationException();
            foreach (var pair in incoming.ToArray())
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)) incoming[pair.Key] = null;
            if (request is CriarAgendamentoToolRequest && incoming["clienteId"] is null)
            {
                var nameChanged = incoming["nomeCliente"] is { } name && !JsonNode.DeepEquals(name, merged["nomeCliente"]);
                var phoneChanged = incoming["telefoneCliente"] is { } phone && !JsonNode.DeepEquals(phone, merged["telefoneCliente"]);
                if (nameChanged || phoneChanged) merged["clienteId"] = null;
                if (nameChanged && incoming["telefoneCliente"] is null) merged["telefoneCliente"] = null;
            }
            foreach (var pair in incoming)
                if (pair.Value is not null) merged[pair.Key] = pair.Value.DeepClone();
            request = AgentRequestJson.Deserialize(merged.ToJsonString());
        }
        if (pending is null)
        {
            var start = await conversations.StartAsync(UserId, intent, null, [], false, clock.GetUtcNow(), ct);
            if (start.Status != PendingIntentStartStatus.Started)
                return AgentToolResult.ConflictResult("Não foi possível iniciar o pedido. Retome ou descarte o pedido pendente.");
        }
        return request switch
        {
            CadastrarClienteToolRequest r => await PrepararCadastroAsync(r, ct),
            CriarAgendamentoToolRequest r => await PrepararCriacaoAsync(r, ct),
            AlterarAgendamentoToolRequest r => await PrepararAlteracaoAsync(r, ct),
            _ => AgentToolResult.Failed("Pedido inválido.")
        };
    }

    private async Task<AgentToolResult> PrepararCadastroAsync(CadastrarClienteToolRequest r, CancellationToken ct)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Nome)) missing.Add("nome");
        if (string.IsNullOrWhiteSpace(r.Telefone)) missing.Add("telefone com DDD");
        await SavePendingAsync(r, missing, null, ct);
        return missing.Count > 0 ? Missing(missing) : await PersistirAsync(r, ct);
    }

    private async Task<AgentToolResult> PrepararCriacaoAsync(CriarAgendamentoToolRequest r, CancellationToken ct, bool somentePerguntar = false)
    {
        ResultadoLocalizacaoCliente? found = r.ClienteId is { } id
            ? await clientes.LocalizarPorIdAsync(id, ct)
            : !string.IsNullOrWhiteSpace(r.TelefoneCliente) ? await clientes.LocalizarPorTelefoneAsync(r.TelefoneCliente, ct)
            : !string.IsNullOrWhiteSpace(r.NomeCliente) ? await clientes.LocalizarPorNomeAsync(r.NomeCliente, ct) : null;
        if (found?.Estado == EstadoLocalizacaoCliente.Ambiguo)
        {
            await SavePendingAsync(r, ["clienteId"], new Dictionary<string, IReadOnlyList<long>> { ["cliente"] = found.Clientes.Select(c => c.Id).ToArray() }, ct);
            return AgentToolResult.Ambiguity("Selecione o cliente: " + string.Join("; ", found.Clientes.Select(c => $"{c.Id}: {c.Nome} ({c.TelefoneNormalizado})")) + ". Envie cliente <ID>.");
        }
        if (found?.Estado == EstadoLocalizacaoCliente.Unico)
        {
            var c = found.Clientes[0];
            r = r with { ClienteId = c.Id, NomeCliente = c.Nome, TelefoneCliente = c.TelefoneNormalizado };
        }
        else if (r.ClienteId is not null)
            return AgentToolResult.ConflictResult("Cliente não encontrado. Localize novamente o cliente.");

        var tipo = (await catalogo.ListarAsync(ct)).SingleOrDefault(t => t.Id == r.TipoServicoId);
        var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
        var clarifyPeriod = pending?.CollectedFields.GetValueOrDefault("periodoRestante") == "true";
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(r.NomeCliente)) missing.Add("nome do cliente");
        if (string.IsNullOrWhiteSpace(r.TelefoneCliente)) missing.Add("telefone com DDD");
        if (tipo is null) missing.Add("serviço desejado");
        if (r.Data is null) missing.Add("data");
        if (clarifyPeriod)
        {
            if (r.Horario is null) missing.Add("horário");
            if (r.Duracao is null) missing.Add("duração");
            if (r.UnidadeDuracao is null) missing.Add("unidade da duração");
        }
        var effective = r;
        var horarioDivergente = false;
        if (tipo is not null)
        {
            // Keep inferred catalog defaults out of the collected request. A later service change
            // must use that service's defaults unless an override was actually collected.
            effective = r with { Modalidade = r.Modalidade ?? tipo.ModalidadePadrao.ToString(), Duracao = r.Duracao ?? (clarifyPeriod ? null : tipo.DuracaoPadrao),
                UnidadeDuracao = r.UnidadeDuracao ?? (clarifyPeriod ? null : tipo.UnidadeDuracaoPadrao.ToString()) };
            if (TryEnum<UnidadeDuracao>(effective.UnidadeDuracao, out var unit) && unit == UnidadeDuracao.Minutos && r.Horario is null) missing.Add("horário");
            horarioDivergente = unit == UnidadeDuracao.DiasCorridos && r.Horario is { } inicio && inicio != new TimeOnly(8, 0);
            if (TryEnum<ModalidadeServico>(effective.Modalidade, out var mode) && mode == ModalidadeServico.Externo && string.IsNullOrWhiteSpace(r.EnderecoServico))
            {
                var endereco = found?.Clientes.FirstOrDefault()?.EnderecoPrincipal;
                if (string.IsNullOrWhiteSpace(endereco)) missing.Add("endereço do serviço");
            }
        }
        if (horarioDivergente) missing.Add("horário de início");
        await SavePendingAsync(r, missing, null, ct);
        if (missing.Count == 0) return somentePerguntar
            ? AgentToolResult.Missing("Os dados do pedido estão completos. Informe se deseja prosseguir com o agendamento.")
            : await PersistirAsync(effective, ct);
        var campos = missing.Where(c => c != "horário de início").ToArray();
        var mensagem = campos.Length == 0 ? "" : CollectionQuestion(campos[0]);
        var clarification = "";
        if (horarioDivergente)
            clarification = $"Você pediu início às {r.Horario:HH:mm}. Serviços em dias corridos reservam 08h–18h em cada data, inclusive a primeira. " +
                (campos.Length == 0 ? "Podemos iniciar às 08h?" : "Precisaremos esclarecer o início às 08h antes de agendar.");
        if (clarification.Length > 0) mensagem = (mensagem + " " + clarification).Trim();
        var result = AgentToolResult.Missing(mensagem) with
        {
            DialogueMode = campos.Length > 0 ? AgentDialogueMode.Collection : AgentDialogueMode.Protected,
            QuestionTopic = campos.Length > 0 ? Topic(campos[0]) : null,
            RequiredFacts = string.IsNullOrWhiteSpace(clarification) ? null : clarification
        };
        if (horarioDivergente) turn.ClarificationResult = result;
        return result;
    }

    private async Task<AgentToolResult> PrepararAlteracaoAsync(AlterarAgendamentoToolRequest r, CancellationToken ct)
    {
        if (r.Operacao?.ToLowerInvariant() is not ("editar" or "reagendar" or "concluir" or "cancelar"))
        {
            await SavePendingAsync(r, ["operação"], null, ct);
            return AgentToolResult.Missing("Informe editar, reagendar, concluir ou cancelar.");
        }
        var hasChanges = r.Data is not null || r.Horario is not null || r.Observacoes is not null || r.EnderecoServico is not null ||
            r.TipoServicoId is not null || r.Modalidade is not null || r.Duracao is not null || r.UnidadeDuracao is not null ||
            r.AlterarEndereco == true || r.AlterarObservacoes == true;
        if ((r.Operacao.ToLowerInvariant() is "concluir" or "cancelar") && hasChanges)
            return AgentToolResult.ConflictResult("Peça a edição separadamente do encerramento do agendamento.");
        if ((r.Operacao.ToLowerInvariant() is "editar" or "reagendar") && !hasChanges)
        {
            await SavePendingAsync(r, ["alteracao"], null, ct);
            return AgentToolResult.Missing("Qual data, horário ou dado deseja alterar?");
        }
        if (r.AgendamentoId is null)
        {
            var resultado = await consulta.ConsultarAsync(new(r.DataBusca, ClienteId: r.ClienteId, Estado: EstadoAgendamento.Agendado), ct);
            if (resultado.Estado == EstadoConsultaAgendamentos.Multiplos || resultado.MaisResultados)
            {
                await SavePendingAsync(r, ["agendamentoId"], new Dictionary<string, IReadOnlyList<long>>
                    { ["agendamento"] = resultado.Agendamentos.Select(a => a.Id).ToArray() }, ct);
                return AgentToolResult.Ambiguity("Selecione um agendamento: " + string.Join("; ", resultado.Agendamentos.Select(a => $"{a.Id}: {a.ClienteNome}, {a.TipoServicoNome}")) + ". Envie agendamento <ID>.");
            }
            if (resultado.Estado != EstadoConsultaAgendamentos.Unico)
            {
                await SavePendingAsync(r, ["agendamentoId"], null, ct);
                return AgentToolResult.Missing("Informe o ID do agendamento ou refine a busca por cliente/data.");
            }
            r = r with { AgendamentoId = resultado.Agendamentos[0].Id, VersaoEsperada = resultado.Agendamentos[0].Versao };
        }
        var atual = await consulta.ConsultarAsync(new(AgendamentoId: r.AgendamentoId), ct);
        if (atual.Estado != EstadoConsultaAgendamentos.Unico) return AgentToolResult.ConflictResult("Agendamento não encontrado.");
        r = r with { VersaoEsperada = r.VersaoEsperada ?? atual.Agendamentos[0].Versao };
        await SavePendingAsync(r, [], null, ct);
        if (r.Operacao.Equals("cancelar", StringComparison.OrdinalIgnoreCase))
        {
            // No model-provided acceptance is accepted here.
            var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
            if (pending is null) return AgentToolResult.ConflictResult("Refaça o pedido de cancelamento.");
            var result = await encerramento.PrepararCancelamentoAsync(RequiredCaller(), pending.Id, r.AgendamentoId.Value, ct);
            return result.Data is ConfirmationChallenge challenge
                ? AgentToolResult.RequiresConfirmation($"Confirma o cancelamento do agendamento {challenge.AgendamentoId}? Envie confirmar {challenge.ChallengeId:N}.")
                : result;
        }
        return await PersistirAsync(r, ct);
    }

    private async Task SavePendingAsync(AgentToolRequest request, IReadOnlyList<string> missing,
        IReadOnlyDictionary<string, IReadOnlyList<long>>? candidates, CancellationToken ct)
    {
        var fields = new Dictionary<string, string> { ["request"] = AgentRequestJson.Serialize(request) };
        if (request is AlterarAgendamentoToolRequest r)
        {
            fields["agendamentoId"] = r.AgendamentoId?.ToString(CultureInfo.InvariantCulture) ?? "";
            fields["operacao"] = r.Operacao?.ToLowerInvariant() ?? "";
        }
        var result = await conversations.UpdateAsync(UserId, fields, missing.Select(field => new string(field.Where(char.IsAsciiLetterOrDigit).ToArray())).ToArray(), candidates, clock.GetUtcNow(), ct);
        if (result != PendingIntentUpdateStatus.Updated) throw new InvalidOperationException("Não foi possível persistir os dados pendentes.");
    }

    private async Task<AgentToolResult> PersistirAsync(AgentToolRequest request, CancellationToken ct)
    {
        if (!await turns.FreezeCommandAsync(turn.UpdateId, AgentRequestJson.Serialize(request), ct))
            return AgentToolResult.ConflictResult("Este update já está vinculado a outra operação. Consulte a agenda antes de um novo pedido.");
        turn.WriteAttempted = true;
        if (request is CriarAgendamentoToolRequest)
        {
            var recorded = await conversations.UpdateAsync(UserId,
                new Dictionary<string, string> { ["executionUpdateId"] = turn.UpdateId.ToString(CultureInfo.InvariantCulture) },
                [], null, clock.GetUtcNow(), ct);
            if (recorded != PendingIntentUpdateStatus.Updated) throw new InvalidOperationException("Pedido de criação ausente.");
        }
        AgentToolResult result;
        switch (request)
        {
            case CadastrarClienteToolRequest r:
                var cliente = await clientes.CadastrarAsync(new(r.Nome, r.Telefone, r.EnderecoPrincipal), ct);
                result = cliente.Cliente is { } c ? AgentToolResult.Succeeded(cliente.Estado == EstadoCadastroCliente.Cadastrado
                    ? $"Cadastrei {c.Nome} com o telefone {c.TelefoneNormalizado}."
                    : $"Encontrei o cadastro de {c.Nome}, telefone {c.TelefoneNormalizado}.")
                    : AgentToolResult.Missing("Informe nome e telefone brasileiro válido com DDD.");
                break;
            case CriarAgendamentoToolRequest r:
                if (!TryEnum<ModalidadeServico>(r.Modalidade, out var modalidade) || !TryEnum<UnidadeDuracao>(r.UnidadeDuracao, out var unidade))
                    return AgentToolResult.Failed("Modalidade ou unidade de duração inválida.");
                if (r.Data is null || r.TipoServicoId is null) return AgentToolResult.Missing("Informe serviço e data.");
                var criado = await criacao.CriarAsync(new(turn.UpdateId, new(r.NomeCliente, r.TelefoneCliente), r.TipoServicoId.Value,
                    r.Data.Value, r.Horario, modalidade, r.Duracao, unidade, r.EnderecoServico, r.Observacoes), ct);
                result = criado.Estado switch
                {
                    EstadoCriacaoAgendamento.Criado or EstadoCriacaoAgendamento.Reentrega when criado.AgendamentoId is { } salvo => await ResumoAsync(salvo, "Agendamento criado", ct),
                    EstadoCriacaoAgendamento.RequerEsclarecimento => AgentToolResult.Missing("Serviços em dias corridos reservam 08h–18h em cada data. Podemos iniciar às 08h?"),
                    EstadoCriacaoAgendamento.Conflito => AgentToolResult.ConflictResult("Não consegui agendar: há conflito com outro compromisso nesse período. Escolha outra data ou horário."),
                    EstadoCriacaoAgendamento.DadosInvalidos => AgentToolResult.Missing("Revise os dados do cliente, endereço, duração e horário dentro de 08h–18h."),
                    _ => AgentToolResult.Failed("Não foi possível criar o agendamento.")
                };
                break;
            case AlterarAgendamentoToolRequest r when r.AgendamentoId is { } id && r.VersaoEsperada is { } version:
                if (r.Operacao?.ToLowerInvariant() == "concluir") result = await encerramento.ConcluirAsync(new(turn.UpdateId, id, version), ct);
                else if (r.Operacao?.ToLowerInvariant() is "editar" or "reagendar")
                {
                    ModalidadeServico? modalidadeEditada = null;
                    UnidadeDuracao? unidadeEditada = null;
                    if (r.Modalidade is not null)
                    {
                        if (!TryEnum<ModalidadeServico>(r.Modalidade, out var m)) return AgentToolResult.Failed("Modalidade inválida.");
                        modalidadeEditada = m;
                    }
                    if (r.UnidadeDuracao is not null)
                    {
                        if (!TryEnum<UnidadeDuracao>(r.UnidadeDuracao, out var u)) return AgentToolResult.Failed("Unidade de duração inválida.");
                        unidadeEditada = u;
                    }
                    var editado = await edicao.EditarAsync(new(turn.UpdateId, id, version, r.Data, r.Horario, r.TipoServicoId,
                        modalidadeEditada, r.Duracao, unidadeEditada, r.EnderecoServico, r.Observacoes, r.AlterarEndereco == true, r.AlterarObservacoes == true), ct);
                    result = editado.Estado switch
                    {
                        EstadoEdicaoAgendamento.Editado or EstadoEdicaoAgendamento.Reentrega => await ResumoAsync(id, "Agendamento atualizado", ct),
                        EstadoEdicaoAgendamento.RequerEsclarecimento => AgentToolResult.Missing("Esse serviço reserva dias completos, das 08h às 18h. Podemos iniciar às 08h?"),
                        _ => AgentToolResult.ConflictResult("Não consegui alterar o agendamento: os dados podem ter mudado ou há conflito de horário. Consulte a agenda e revise o pedido.")
                    };
                }
                else result = AgentToolResult.ConflictResult("Cancelamento exige confirmação explícita vinculada ao desafio.");
                break;
            case ConfirmarCancelamentoCommand r:
                result = await encerramento.CancelarAsync(new(turn.UpdateId, RequiredCaller(), r.Acceptance), ct);
                break;
            default: result = AgentToolResult.Missing("Complete os dados do pedido."); break;
        }
        if (result.Outcome == AgentToolOutcome.Success && request is AlterarAgendamentoToolRequest { Operacao: "concluir", AgendamentoId: { } concluido })
            result = await ResumoAsync(concluido, "Agendamento concluído", ct);
        if (result.Outcome == AgentToolOutcome.Success && request is ConfirmarCancelamentoCommand cancelamento &&
            cancelamento.Acceptance.Arguments.AgendamentoId is { } cancelado)
            result = await ResumoAsync(cancelado, "Agendamento cancelado", ct);
        if (result.Outcome == AgentToolOutcome.Error && result.FailureCategory == AgentFailureCategory.Unknown)
            result = result with { FailureCategory = AgentFailureCategory.ToolFailure };
        turn.MutationResult = result;
        if (result.Outcome == AgentToolOutcome.Success)
        {
            await turns.CompletePendingAsync(UserId, ct);
        }
        return result;
    }

    private async Task<AgentToolResult> LocalizarAsync(LocalizarClienteToolRequest r, CancellationToken ct)
    {
        var result = !string.IsNullOrWhiteSpace(r.Telefone) ? await clientes.LocalizarPorTelefoneAsync(r.Telefone, ct)
            : await clientes.LocalizarPorNomeAsync(r.Nome, ct);
        if (result.Estado == EstadoLocalizacaoCliente.Ambiguo)
        {
            turn.SelectionRequired = true;
            var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
            if (pending is null)
                await conversations.StartAsync(UserId, PendingIntent.LocalizarCliente,
                    new Dictionary<string, string> { ["request"] = AgentRequestJson.Serialize(r) }, ["clienteId"], false, clock.GetUtcNow(), ct);
            await conversations.UpdateAsync(UserId, new Dictionary<string, string>(), pending?.MissingFields.Append("clienteId").Distinct().ToArray() ?? ["clienteId"],
                new Dictionary<string, IReadOnlyList<long>> { ["cliente"] = result.Clientes.Select(c => c.Id).ToArray() }, clock.GetUtcNow(), ct);
            return AgentToolResult.Ambiguity("Selecione um cliente: " + string.Join("; ", result.Clientes.Select(c => $"{c.Id}: {c.Nome}, telefone {c.TelefoneNormalizado}")) + ". Envie cliente <ID>.");
        }
        return result.Estado == EstadoLocalizacaoCliente.Invalido ? AgentToolResult.Missing("Informe nome ou telefone brasileiro com DDD.")
            : AgentToolResult.Succeeded(result.Clientes.Count == 0 ? "Nenhum cliente encontrado."
                : string.Join("; ", result.Clientes.Select(c => $"{c.Nome}, telefone {c.TelefoneNormalizado}")) + (result.MaisResultados ? ". Refine a busca." : ""), result.Clientes);
    }

    private async Task<AgentToolResult> ConsultarAsync(ConsultarAgendaToolRequest r, CancellationToken ct)
    {
        if (r.DataInicial is null && r.ClienteId is null && r.AgendamentoId is null)
            return AgentToolResult.Missing("Qual data ou período deseja consultar?");
        var result = await consulta.ConsultarAsync(new(r.DataInicial, r.DataFinal, r.ClienteId, AgendamentoId: r.AgendamentoId, Limite: 10), ct);
        if (result.Estado == EstadoConsultaAgendamentos.Invalido) return AgentToolResult.Missing("Informe um período válido.");
        if (result.Agendamentos.Count == 0) return AgentToolResult.Succeeded("Nenhum agendamento encontrado para essa consulta. Quer verificar outra data?");
        var periodos = result.Agendamentos.SelectMany(a => a.Periodos).ToArray();
        var inicio = r.DataInicial ?? periodos.Min(p => p.Data);
        var fim = r.DataFinal ?? (r.DataInicial is null ? periodos.Max(p => p.Data) : inicio);
        // Do not enumerate thousands of civil dates in the text formatter.
        if (fim.DayNumber - inicio.DayNumber > 31) return AgentToolResult.Missing("Refine a consulta para até 31 dias.");
        return AgentToolResult.Succeeded(FormatadorAgenda.Formatar(inicio, fim, result.Agendamentos)
            + (result.MaisResultados ? "\nHá mais resultados; refine os filtros." : ""),
            result.Agendamentos.Select(a => new { a.Id, a.Versao, Estado = a.Estado.ToString() }).ToArray());
    }

    private async Task<AgentToolResult> DisponibilidadeAsync(VerificarDisponibilidadeToolRequest r, CancellationToken ct)
    {
        if (r.Data is null) return AgentToolResult.Missing("Qual data deseja verificar?");
        if (r.Duracao is null) return AgentToolResult.Missing("Por quanto tempo você precisa reservar?");
        if (!TryEnum<UnidadeDuracao>(r.UnidadeDuracao, out var unidade))
            return AgentToolResult.Missing("Essa duração é em minutos ou dias corridos?");
        var result = await disponibilidade.VerificarAsync(new(r.Data.Value, r.Horario, r.Duracao.Value, unidade), ct);
        return result.Estado switch
        {
            EstadoDisponibilidadeAgenda.Disponivel => AgentToolResult.Succeeded("Disponível: " + string.Join("; ", result.Periodos.Take(14).Select(p => $"{p.Data:dd/MM/yyyy} {p.Inicio:HH:mm}–{p.Fim:HH:mm}"))),
            EstadoDisponibilidadeAgenda.Indisponivel => AgentToolResult.ConflictResult("Período ocupado. Escolha outro horário/data."),
            EstadoDisponibilidadeAgenda.RequerEsclarecimento => AgentToolResult.Missing("Esse serviço reserva dias completos, das 08h às 18h. Podemos iniciar às 08h?"),
            _ => AgentToolResult.Missing("Informe horário dentro de 08h–18h e duração positiva.")
        };
    }

    private AgentToolCaller RequiredCaller() => turn.Caller ?? throw new InvalidOperationException("Ator ausente.");
    public async Task<AgentToolResult> ComposeDialogueAsync(AgentDialoguePlan plan, CancellationToken ct)
    {
        // Persistence, refusals, explicit selection and confirmation always outrank conversational acts.
        if (turn.MutationResult is { } mutation) return mutation;
        if (turn.PendingResult is { Outcome: AgentToolOutcome.Ambiguous or AgentToolOutcome.Conflict or AgentToolOutcome.ConfirmationRequired or AgentToolOutcome.Error } blocked) return blocked;
        if (turn.LastResult is { Outcome: AgentToolOutcome.Error or AgentToolOutcome.Conflict or AgentToolOutcome.Ambiguous or AgentToolOutcome.ConfirmationRequired } refused) return refused;
        var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
        if (pending?.CandidateIds.Count > 0) return await EsclarecerAsync(ct);
        if (plan.Objetivo == DialogueGoal.Conversa && plan.Resposta is DialogueReply.Esclarecer or DialogueReply.Resultado)
            return AgentToolResult.Missing("Estou por aqui! Como posso ajudar?") with { DialogueMode = AgentDialogueMode.Social };
        AgentToolResult? pergunta = turn.PendingResult;
        if (pending?.Intent == PendingIntent.CriarAgendamento &&
            pending.CollectedFields.GetValueOrDefault("request") is { } request)
            pergunta = await PrepararCriacaoAsync((CriarAgendamentoToolRequest)AgentRequestJson.Deserialize(request), ct, somentePerguntar: true);
        var catalogRequested = PendingTurnDataCapture.IsCatalogRequest(turn.Text);
        if (catalogRequested || plan.Resposta == DialogueReply.ExplicarCatalogo && turn.CatalogWasShown &&
            Regex.IsMatch(turn.Text, @"\b(o que é|o que e|explique|não entendi|nao entendi)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var catalog = await ServicosAsync(ct);
            var texto = "Essa é a lista de serviços que você pode agendar. As durações são os padrões de cada serviço e podem ser informadas no pedido.\n" + catalog.Message;
            texto += "\nServiços em dias corridos reservam 08h–18h em cada data, inclusive a primeira.";
            return AgentToolResult.Succeeded(texto + "\n" + (pergunta?.Message ?? "Qual serviço você deseja agendar?")) with
                { DialogueMode = AgentDialogueMode.Explanation };
        }
        if (plan.Resposta == DialogueReply.ExplicarCatalogo && pergunta is not null)
            return AgentToolResult.Missing("Estou pedindo o próximo dado necessário para continuar seu agendamento. " + pergunta.Message);
        if (plan.Resposta == DialogueReply.ExplicarPedido && pergunta is not null)
            return AgentToolResult.Missing("Esses dados são necessários para reservar o serviço corretamente. " + pergunta.Message) with
                { DialogueMode = AgentDialogueMode.Explanation };
        if (pergunta is not null && (plan.Objetivo is DialogueGoal.CriarAgendamento or DialogueGoal.Continuar || plan.Resposta == DialogueReply.Coletar))
            return pergunta;
        return turn.PendingResult ?? turn.LastResult ?? pergunta ?? AgentToolResult.Missing("Qual informação da agenda você deseja consultar ou organizar?");
    }
    private async Task<AgentToolResult> EsclarecerAsync(CancellationToken ct)
    {
        var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
        if (pending is null) return AgentToolResult.Missing("Como posso ajudar com sua agenda?") with { DialogueMode = AgentDialogueMode.Social };
        if (pending.CandidateIds.Count > 0)
            return AgentToolResult.Ambiguity("Para continuar seu pedido, selecione um dos clientes ou agendamentos apresentados.");
        // An unclear turn must neither discard the goal nor execute an otherwise complete request.
        return AgentToolResult.Missing(pending.Intent == PendingIntent.CriarAgendamento
            ? "Seu agendamento continua pendente. Você pode completar os dados solicitados ou me dizer o que deseja esclarecer."
            : "Seu pedido continua pendente. Você pode completar os dados solicitados ou me dizer o que deseja esclarecer.") with
                { DialogueMode = AgentDialogueMode.Explanation };
    }
    private async Task<AgentToolResult> ServicosAsync(CancellationToken ct)
    {
        var tipos = await catalogo.ListarAsync(ct);
        return turn.CatalogResult = AgentToolResult.Succeeded("Serviços: " + string.Join("; ",
            tipos.Select(t => $"{t.Nome}: {Duracao(t.DuracaoPadrao, t.UnidadeDuracaoPadrao)}")),
            tipos.Select(t => new { t.Id, t.Nome, t.DuracaoPadrao,
                UnidadeDuracaoPadrao = t.UnidadeDuracaoPadrao.ToString(), ModalidadePadrao = t.ModalidadePadrao.ToString() }).ToArray());
    }
    private async Task<AgentToolResult> ResumoAsync(long id, string titulo, CancellationToken ct)
    {
        var resultado = await consulta.ConsultarAsync(new(AgendamentoId: id), ct);
        var a = resultado.Agendamentos.SingleOrDefault();
        if (a is null) return AgentToolResult.Failed("Não foi possível consultar o agendamento salvo.");
        var periodos = a.Periodos.OrderBy(p => p.Data).ThenBy(p => p.Inicio).ToArray();
        var datas = periodos[0].Data == periodos[^1].Data
            ? periodos[0].Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : $"{periodos[0].Data:dd/MM/yyyy} a {periodos[^1].Data:dd/MM/yyyy}";
        var horarios = a.UnidadeDuracao == UnidadeDuracao.DiasCorridos ? "08h–18h em cada dia"
            : string.Join("; ", periodos.Select(p => $"{p.Data:dd/MM/yyyy} {p.Inicio:HH:mm}–{p.Fim:HH:mm}"));
        var endereco = a.EnderecoServico is null ? "" : $"\nEndereço: {a.EnderecoServico}.";
        var observacoes = string.IsNullOrWhiteSpace(a.Observacoes) ? "" : $"\nObservações: {a.Observacoes}";
        var details = $"{a.TipoServicoNome} para {a.ClienteNome}, {datas}.\n{Duracao(a.Duracao, a.UnidadeDuracao)} – {horarios}.{endereco}{observacoes}";
        return AgentToolResult.Succeeded($"{titulo} (#{a.Id}): {details}");
    }
    private static string Duracao(int valor, UnidadeDuracao unidade) => unidade == UnidadeDuracao.DiasCorridos
        ? $"{valor} {(valor == 1 ? "dia corrido" : "dias corridos")}" : $"{valor} minutos";
    private static string CollectionQuestion(string field) => field switch
    {
        "nome do cliente" or "nome" => "Qual é o nome do cliente?",
        "telefone com DDD" or "telefone" => "Qual é o telefone com DDD do cliente?",
        "serviço desejado" => "Qual serviço você deseja agendar?",
        "data" => "Para qual data você quer agendar?",
        "horário" => "Qual horário você prefere?",
        "duração" => "Por quanto tempo você precisa do serviço?",
        "unidade da duração" => "Essa duração é em minutos ou dias corridos?",
        "endereço do serviço" or "endereço principal" => "Qual é o endereço do serviço?",
        _ => $"Pode informar {field} para continuarmos?"
    };
    private static AgentToolResult Missing(IReadOnlyList<string> fields) =>
        AgentToolResult.Missing(CollectionQuestion(fields[0])) with
            { DialogueMode = AgentDialogueMode.Collection, QuestionTopic = Topic(fields[0]) };
    private static string Topic(string field) => field switch
    {
        "nome do cliente" => "nome", "telefone com DDD" => "telefone", "serviço desejado" => "serviço",
        "endereço do serviço" or "endereço principal" => "endereço", "unidade da duração" => "unidade", _ => field
    };
    private static bool TryEnum<T>(string? value, out T parsed) where T : struct, Enum =>
        Enum.TryParse(value, true, out parsed) && Enum.IsDefined(parsed) && Enum.GetNames<T>().Contains(value, StringComparer.OrdinalIgnoreCase);
}
