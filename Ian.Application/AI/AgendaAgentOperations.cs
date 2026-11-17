using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Ian.Application.Agenda;
using Ian.Domain.Entities;

namespace Ian.Application.AI;

[JsonConverter(typeof(JsonStringEnumConverter<AppointmentOperation>))]
public enum AppointmentOperation { Editar, Reagendar, Concluir, Cancelar }
public enum AppointmentField { Endereco, Duracao, Modalidade, Observacoes, Data, Horario }

public sealed partial class AgendaAgentApplication
{
    private async Task<AgentToolResult> PrepararAlteracaoAsync(AlterarAgendamentoToolRequest r, CancellationToken ct)
    {
        if (!TryEnum<AppointmentOperation>(r.Operacao, out var operation))
        {
            await SavePendingAsync(r, ["operação"], null, ct);
            return AgentToolResult.Missing("Informe editar, reagendar, concluir ou cancelar.");
        }
        r = r with { Operacao = operation.ToString().ToLowerInvariant() };
        var pending = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
        var selectedInPending = pending?.CollectedFields.GetValueOrDefault("agendamentoId");
        var references = turn.Operational;
        var ambiguousReference = references.SelectedAppointment is null && (references.Appointments.Count > 1 || references.MoreAppointments);
        // A model-provided ID is not acceptance of a choice between multiple candidates.
        var explicitId = r.AgendamentoId is { } target && (selectedInPending == target.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            HasExplicitAppointmentId(turn.Text, target));
        if (ambiguousReference && !explicitId && (r.UsarContexto == true || r.AgendamentoId is not null))
            return await PedirSelecaoAsync(r with { AgendamentoId = null }, references.Appointments, ct);
        if (r.UsarContexto == true && r.AgendamentoId is { } supplied && !explicitId && references.SelectedAppointment?.Id != supplied)
            return AgentToolResult.ConflictResult("O ID não corresponde à referência selecionada. Consulte ou selecione o agendamento.");
        if (r.UsarContexto == true && r.AgendamentoId is null)
        {
            if (references.SelectedAppointment is not { } selected)
            {
                await SavePendingAsync(r, ["agendamentoId"], null, ct);
                return AgentToolResult.Missing("Consulte ou selecione o agendamento para resolver essa referência.");
            }
            r = r with { AgendamentoId = selected.Id, VersaoEsperada = r.VersaoEsperada ?? selected.Version };
        }
        if (r.AgendamentoId is null)
        {
            var found = await consulta.ConsultarAsync(new(r.DataBusca, ClienteId: r.ClienteId, Estado: EstadoAgendamento.Agendado), ct);
            if (found.Estado == EstadoConsultaAgendamentos.Multiplos || found.MaisResultados)
                return await PedirSelecaoAsync(r, found.Agendamentos.Select(AppointmentReference.From).ToArray(), ct, found.Agendamentos);
            if (found.Estado != EstadoConsultaAgendamentos.Unico)
            {
                await SavePendingAsync(r, ["agendamentoId"], null, ct);
                return AgentToolResult.Missing("Informe o ID do agendamento ou refine a busca por cliente/data.");
            }
            r = r with { AgendamentoId = found.Agendamentos[0].Id, VersaoEsperada = found.Agendamentos[0].Versao };
        }
        var result = await consulta.ConsultarAsync(new(AgendamentoId: r.AgendamentoId), ct);
        if (result.Estado != EstadoConsultaAgendamentos.Unico) return AgentToolResult.ConflictResult("Agendamento não encontrado.");
        var current = result.Agendamentos[0];
        r = r with { VersaoEsperada = r.VersaoEsperada ?? current.Versao };
        var changes = r.Data is not null || r.Horario is not null || r.Observacoes is not null || r.EnderecoServico is not null ||
            r.TipoServicoId is not null || r.Modalidade is not null || r.Duracao is not null || r.DeltaDuracao is not null ||
            r.UnidadeDuracao is not null || r.AlterarEndereco == true || r.AlterarObservacoes == true || r.CampoPendente is not null;
        if (operation is AppointmentOperation.Concluir or AppointmentOperation.Cancelar && changes)
            return AgentToolResult.ConflictResult("Peça a edição separadamente do encerramento do agendamento.");
        if (r.CampoPendente is { } field)
        {
            if (!TryEnum<AppointmentField>(field, out var parsed)) return AgentToolResult.Missing("Informe qual campo deseja alterar.");
            var missing = parsed switch
            {
                AppointmentField.Endereco => r.EnderecoServico is null && r.AlterarEndereco != true,
                AppointmentField.Duracao => r.Duracao is null && r.DeltaDuracao is null,
                AppointmentField.Modalidade => r.Modalidade is null,
                AppointmentField.Observacoes => r.Observacoes is null && r.AlterarObservacoes != true,
                AppointmentField.Data => r.Data is null,
                _ => r.Horario is null
            };
            if (missing)
            {
                await SavePendingAsync(r, [parsed.ToString()], null, ct);
                return AgentToolResult.Missing(parsed == AppointmentField.Endereco ? "Qual é o novo endereço do serviço?" : $"Informe o novo valor de {field}.");
            }
            r = r with { CampoPendente = null };
        }
        if (operation is AppointmentOperation.Editar or AppointmentOperation.Reagendar && !changes)
        {
            await SavePendingAsync(r, ["alteracao"], null, ct);
            return AgentToolResult.Missing("Qual data, horário ou dado deseja alterar?");
        }
        if (r.DeltaDuracao is { } delta)
        {
            if (r.Duracao is not null) return await CorrigirDuracaoAsync(r, "Informe duração absoluta ou variação relativa, separadamente.", ct);
            if (!TryEnum<UnidadeDuracao>(r.UnidadeDuracao, out var unit) || unit != current.UnidadeDuracao)
                return await CorrigirDuracaoAsync(r, "Informe a variação na mesma unidade da duração atual, sem conversão implícita.", ct);
            var duration = (long)current.Duracao + delta;
            if (delta == 0 || duration is <= 0 or > 720) return await CorrigirDuracaoAsync(r, "A alteração precisa resultar em duração positiva dentro dos limites.", ct);
            // Freeze an absolute command and its expected version: retries never apply the delta twice.
            r = r with { Duracao = (int)duration, DeltaDuracao = null };
        }
        await SavePendingAsync(r, [], null, ct);
        if (operation == AppointmentOperation.Cancelar)
        {
            var active = await conversations.GetForTurnAsync(UserId, clock.GetUtcNow(), ct);
            if (active is null) return AgentToolResult.ConflictResult("Refaça o pedido de cancelamento.");
            var cancellation = await encerramento.PrepararCancelamentoAsync(RequiredCaller(), active.Id, current.Id, ct);
            return cancellation.Data is Ian.Application.Conversation.ConfirmationChallenge challenge
                ? AgentToolResult.RequiresConfirmation($"Confirma o cancelamento do agendamento {challenge.AgendamentoId}? Envie confirmar {challenge.ChallengeId:N}.")
                : cancellation;
        }
        return await PersistirAsync(r, ct);
    }

    private async Task<AgentToolResult> PedirSelecaoAsync(AlterarAgendamentoToolRequest request, IReadOnlyList<AppointmentReference> items, CancellationToken ct,
        IReadOnlyList<AgendamentoConsultaItem>? descriptions = null)
    {
        await SavePendingAsync(request, ["agendamentoId"], new Dictionary<string, IReadOnlyList<long>>
            { ["agendamento"] = items.Select(a => a.Id).ToArray() }, ct);
        var options = descriptions is null ? string.Join("; ", items.Select(a => $"#{a.Id}"))
            : string.Join("; ", descriptions.Select(a => $"#{a.Id}: {a.ClienteNome}, {a.TipoServicoNome}, {a.Periodos[0].Data:dd/MM/yyyy}"));
        return AgentToolResult.Ambiguity("Selecione um agendamento: " + options + ". Envie agendamento <ID>.");
    }

    private async Task<AgentToolResult> CorrigirDuracaoAsync(AlterarAgendamentoToolRequest request, string message, CancellationToken ct)
    {
        await SavePendingAsync(request, ["duração"], null, ct);
        return AgentToolResult.Missing(message);
    }

    private static bool HasExplicitAppointmentId(string text, long id) => Regex.IsMatch(text,
        @"(?:\bagendamento\s*|#)" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
