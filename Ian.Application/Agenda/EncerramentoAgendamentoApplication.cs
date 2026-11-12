using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public sealed class EncerramentoAgendamentoApplication(
    IAgendaEncerramentoStore store,
    IAgendaMutationExecutor mutations,
    IOperationConfirmation confirmations,
    TimeProvider clock) : IEncerramentoAgendamento
{
    public const string ConcluirOperationKey = "agendamento:complete";

    public Task<AgentToolResult> PrepararCancelamentoAsync(
        AgentToolCaller ator,
        long solicitacaoPendenteId,
        long agendamentoId,
        CancellationToken cancellationToken = default) =>
        confirmations.PrepareAsync(ator, solicitacaoPendenteId,
            new(agendamentoId, "cancelar", null, null, null), cancellationToken);

    public Task<AgentToolResult> CancelarAsync(
        CancelarAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);
        ArgumentNullException.ThrowIfNull(solicitacao.Ator);
        if (solicitacao.Confirmacao is not { } confirmacao)
            return Task.FromResult(AgentToolResult.ConflictResult("Cancelamento exige confirmação explícita válida."));

        // #22 owns authorization, idempotency, the lock, version validation and challenge consumption.
        // Its callback stages the business transition inside that same transaction.
        return confirmations.ExecuteAsync(solicitacao.Ator, solicitacao.UpdateId, confirmacao,
            (operation, token) => EncerrarAsync(operation.AgendamentoId, operation.Version,
                EstadoAgendamento.Cancelado, solicitacao.UpdateId,
                $"confirmation:{confirmacao.PendingRequestId}", token), cancellationToken);
    }

    public async Task<AgentToolResult> ConcluirAsync(
        ConcluirAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);
        if (solicitacao.UpdateId < 0 || solicitacao.AgendamentoId <= 0 || solicitacao.VersaoEsperada <= 0)
            return AgentToolResult.ConflictResult("Informe ID e versão válidos para concluir o agendamento.");

        AgentToolResult? resultado = null;
        try
        {
            var executado = await mutations.ExecuteOnceAsync(solicitacao.UpdateId, ConcluirOperationKey,
                async token =>
                {
                    resultado = await EncerrarAsync(solicitacao.AgendamentoId, solicitacao.VersaoEsperada,
                        EstadoAgendamento.Concluido, solicitacao.UpdateId, ConcluirOperationKey, token);
                    if (resultado.Outcome != AgentToolOutcome.Success)
                        throw new EncerramentoRecusadoException(resultado);
                }, cancellationToken);
            if (!executado)
            {
                var anterior = await store.ObterPorOperacaoAsync(solicitacao.UpdateId, ConcluirOperationKey, cancellationToken)
                    ?? throw new InvalidOperationException("A conclusão idempotente deve identificar o agendamento original.");
                return AgentToolResult.Succeeded("Esta conclusão já foi processada.", anterior);
            }
            return resultado ?? throw new InvalidOperationException("A conclusão confirmada deve produzir um resultado.");
        }
        catch (EncerramentoRecusadoException exception) { return exception.Resultado; }
        catch (AgendaVersionConflictException)
        {
            return AgentToolResult.ConflictResult("O agendamento foi alterado. Releia os dados antes de concluir.");
        }
        catch (AgendaLockUnavailableException)
        {
            return AgentToolResult.ConflictResult("A agenda está ocupada. Tente novamente.");
        }
    }

    private async Task<AgentToolResult> EncerrarAsync(
        long agendamentoId,
        long versaoEsperada,
        EstadoAgendamento destino,
        long updateId,
        string operationKey,
        CancellationToken cancellationToken)
    {
        var atual = await store.ObterAsync(agendamentoId, cancellationToken);
        if (atual is null)
            return AgentToolResult.ConflictResult("Agendamento não encontrado.");
        if (atual.Versao != versaoEsperada)
            return AgentToolResult.ConflictResult("O agendamento foi alterado. Releia os dados antes de encerrar.");
        if (atual.Estado != EstadoAgendamento.Agendado)
            return AgentToolResult.ConflictResult("Somente agendamentos Agendados podem ser concluídos ou cancelados.");

        // Existing availability/creation queries treat only Agendado periods as active.
        // Retaining the planned periods keeps closed appointments discoverable by date and ID.
        atual.Estado = destino;
        atual.IncrementarVersao();
        await store.RegistrarAsync(atual, updateId, operationKey, clock.GetUtcNow(), cancellationToken);
        return AgentToolResult.Succeeded(destino == EstadoAgendamento.Concluido
            ? "Agendamento concluído." : "Agendamento cancelado.", new AgendamentoEncerradoItem(atual.Id, atual.Estado, atual.Versao));
    }

    private sealed class EncerramentoRecusadoException(AgentToolResult resultado) : Exception
    {
        public AgentToolResult Resultado { get; } = resultado;
    }
}
