using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public sealed record ConcluirAgendamentoSolicitacao(long UpdateId, long AgendamentoId, long VersaoEsperada);

public sealed record CancelarAgendamentoSolicitacao(
    long UpdateId, AgentToolCaller Ator, ConfirmationAcceptance? Confirmacao);

public sealed record AgendamentoEncerradoItem(long AgendamentoId, EstadoAgendamento Estado, long Versao);

public interface IEncerramentoAgendamento
{
    // The pending intent must already contain the unambiguous ID and operation, as required by #22.
    Task<AgentToolResult> PrepararCancelamentoAsync(
        AgentToolCaller ator,
        long solicitacaoPendenteId,
        long agendamentoId,
        CancellationToken cancellationToken = default);

    Task<AgentToolResult> CancelarAsync(
        CancelarAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default);

    Task<AgentToolResult> ConcluirAsync(
        ConcluirAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default);
}

public interface IAgendaEncerramentoStore
{
    // Load only after the mutation/confirmation executor has acquired the agenda lock.
    Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default);

    Task RegistrarAsync(
        Agendamento agendamento,
        long updateId,
        string operationKey,
        DateTimeOffset instanteUtc,
        CancellationToken cancellationToken = default);

    Task<AgendamentoEncerradoItem?> ObterPorOperacaoAsync(
        long updateId,
        string operationKey,
        CancellationToken cancellationToken = default);
}
