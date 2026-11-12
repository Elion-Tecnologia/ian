using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public enum EstadoSelecaoEdicao
{
    Nenhum,
    Unico,
    RequerSelecao,
    ConversaEmAndamento,
    Invalido
}

public sealed record ResultadoSelecaoEdicao(
    EstadoSelecaoEdicao Estado,
    IReadOnlyList<AgendamentoConsultaItem> Candidatos,
    bool MaisResultados = false);

/// <summary>Prepares 0/1/N candidates. Selection is persisted through #21's SelectCandidateAsync.</summary>
public sealed class SelecaoEdicaoAgendamentoApplication(IConsultaAgenda consulta, IPendingConversation conversa)
{
    public const string CampoAgendamento = "Agendamento";

    public async Task<ResultadoSelecaoEdicao> BuscarAsync(
        long telegramUserId,
        ConsultaAgendamentosFiltro filtro,
        DateTimeOffset agoraUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        if (telegramUserId <= 0)
            return new(EstadoSelecaoEdicao.Invalido, []);
        var resultado = await consulta.ConsultarAsync(
            filtro with { Estado = EstadoAgendamento.Agendado, Limite = 20 }, cancellationToken);
        if (resultado.Estado == EstadoConsultaAgendamentos.Invalido)
            return new(EstadoSelecaoEdicao.Invalido, []);
        if (resultado.Estado == EstadoConsultaAgendamentos.Nenhum)
            return new(EstadoSelecaoEdicao.Nenhum, []);
        if (resultado.Estado == EstadoConsultaAgendamentos.Unico && !resultado.MaisResultados)
            return new(EstadoSelecaoEdicao.Unico, resultado.Agendamentos);

        // Do not replace an existing intent or silently change its candidate set.
        var inicio = await conversa.StartAsync(telegramUserId, PendingIntent.AlterarAgendamento,
            null, [CampoAgendamento], false, agoraUtc, cancellationToken);
        if (inicio.Status != PendingIntentStartStatus.Started)
            return new(EstadoSelecaoEdicao.ConversaEmAndamento, resultado.Agendamentos, resultado.MaisResultados);
        var atualizado = await conversa.UpdateAsync(telegramUserId,
            new Dictionary<string, string>(), [CampoAgendamento],
            new Dictionary<string, IReadOnlyList<long>>
            {
                [CampoAgendamento] = resultado.Agendamentos.Select(item => item.Id).ToArray()
            }, agoraUtc, cancellationToken);
        return new(atualizado == PendingIntentUpdateStatus.Updated
            ? EstadoSelecaoEdicao.RequerSelecao : EstadoSelecaoEdicao.ConversaEmAndamento,
            resultado.Agendamentos, resultado.MaisResultados);
    }
}
