using Ian.Application.Catalogo;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public sealed class EdicaoAgendamentoApplication(
    IAgendaEdicaoStore store,
    IConsultaAgendaStore consultaStore,
    ITipoServicoCatalogo catalogo,
    IAgendaMutationExecutor mutationExecutor) : IEdicaoAgendamento
{
    public const string OperationKey = "agendamento:edit";
    private static readonly TimeZoneInfo FusoHorario =
        TimeZoneInfo.FindSystemTimeZoneById(CalculadoraOcupacao.FusoHorarioId);

    public async Task<ResultadoEdicaoAgendamento> EditarAsync(
        EditarAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);
        if (solicitacao.UpdateId < 0 || solicitacao.AgendamentoId <= 0 || solicitacao.VersaoEsperada <= 0)
            return new(EstadoEdicaoAgendamento.DadosInvalidos, solicitacao.AgendamentoId,
                Erro: ErroEdicaoAgendamento.IdentificacaoInvalida);

        ResultadoEdicaoAgendamento? resultado = null;
        try
        {
            var executado = await mutationExecutor.ExecuteOnceAsync(
                solicitacao.UpdateId, OperationKey, async token =>
                {
                    var atual = await store.ObterAsync(solicitacao.AgendamentoId, token);
                    if (atual is null) Abortar(EstadoEdicaoAgendamento.NaoEncontrado);
                    if (atual.Versao != solicitacao.VersaoEsperada) Abortar(EstadoEdicaoAgendamento.VersaoObsoleta);
                    if (atual.Estado != EstadoAgendamento.Agendado) Abortar(EstadoEdicaoAgendamento.EstadoNaoPermitido);

                    var tipoId = solicitacao.TipoServicoId ?? atual.TipoServicoId;
                    if (!(await catalogo.ListarAsync(token)).Any(tipo => tipo.Id == tipoId))
                        Abortar(EstadoEdicaoAgendamento.DadosInvalidos, ErroEdicaoAgendamento.TipoServicoNaoEncontrado);
                    var modalidade = solicitacao.Modalidade ?? atual.Modalidade;
                    var unidade = solicitacao.UnidadeDuracao ?? atual.UnidadeDuracao;
                    if (!Enum.IsDefined(modalidade))
                        Abortar(EstadoEdicaoAgendamento.DadosInvalidos, ErroEdicaoAgendamento.ModalidadeInvalida);
                    if (!Enum.IsDefined(unidade))
                        Abortar(EstadoEdicaoAgendamento.DadosInvalidos, ErroEdicaoAgendamento.UnidadeDuracaoInvalida);

                    // Never inherit the customer's current address on an edit.
                    var endereco = solicitacao.AlterarEndereco || solicitacao.EnderecoServico is not null
                        ? Normalizar(solicitacao.EnderecoServico) : atual.EnderecoSnapshot;
                    if (modalidade == ModalidadeServico.Externo && string.IsNullOrWhiteSpace(endereco))
                        Abortar(EstadoEdicaoAgendamento.DadosInvalidos, ErroEdicaoAgendamento.EnderecoExternoObrigatorio);

                    var inicioLocal = TimeZoneInfo.ConvertTime(atual.InicioPrevistoUtc, FusoHorario);
                    var duracao = solicitacao.Duracao ?? atual.Duracao;
                    var calculo = CalculadoraOcupacao.Calcular(
                        solicitacao.Data ?? DateOnly.FromDateTime(inicioLocal.DateTime),
                        solicitacao.Horario ?? TimeOnly.FromDateTime(inicioLocal.DateTime),
                        duracao, unidade);
                    if (calculo.Estado != EstadoCalculoOcupacao.Calculado)
                        throw new EdicaoAbortadaException(new(
                            calculo.Estado == EstadoCalculoOcupacao.RequerEsclarecimento
                                ? EstadoEdicaoAgendamento.RequerEsclarecimento : EstadoEdicaoAgendamento.DadosInvalidos,
                            atual.Id, Erro: calculo.Estado == EstadoCalculoOcupacao.Invalido
                                ? ErroEdicaoAgendamento.CalculoInvalido : null,
                            MotivoCalculo: calculo.Motivo));

                    var conflitos = await consultaStore.ObterConflitosAsync(
                        calculo.Periodos.Select(periodo => new PeriodoAgendaStoreItem(periodo.InicioUtc, periodo.FimUtc)).ToArray(), token);
                    if (conflitos.Any(id => id != atual.Id)) Abortar(EstadoEdicaoAgendamento.Conflito);

                    atual.TipoServicoId = tipoId;
                    atual.Modalidade = modalidade;
                    atual.Duracao = duracao;
                    atual.UnidadeDuracao = unidade;
                    atual.InicioPrevistoUtc = calculo.Periodos[0].InicioUtc;
                    atual.FimPrevistoUtc = calculo.Periodos[^1].FimUtc;
                    atual.EnderecoSnapshot = endereco;
                    if (solicitacao.AlterarObservacoes || solicitacao.Observacoes is not null)
                        atual.Observacoes = Normalizar(solicitacao.Observacoes);
                    atual.IncrementarVersao();
                    await store.SubstituirPeriodosAsync(atual, calculo.Periodos, solicitacao.UpdateId, token);
                    resultado = new(EstadoEdicaoAgendamento.Editado, atual.Id, atual.Versao, calculo.Periodos);
                }, cancellationToken);
            // A replay acknowledges the original update; it does not claim the current state/version.
            return executado
                ? resultado ?? throw new InvalidOperationException("A edição confirmada deve produzir um resultado.")
                : new(EstadoEdicaoAgendamento.Reentrega,
                    await store.ObterIdPorOperacaoAsync(solicitacao.UpdateId, cancellationToken)
                        ?? throw new InvalidOperationException("A edição idempotente deve identificar o agendamento original."));
        }
        catch (EdicaoAbortadaException exception) { return exception.Resultado; }
        catch (AgendaVersionConflictException) { return new(EstadoEdicaoAgendamento.VersaoObsoleta, solicitacao.AgendamentoId); }
        catch (AgendaLockUnavailableException) { return new(EstadoEdicaoAgendamento.AgendaIndisponivel, solicitacao.AgendamentoId); }

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        void Abortar(EstadoEdicaoAgendamento estado, ErroEdicaoAgendamento? erro = null) =>
            throw new EdicaoAbortadaException(new(estado, solicitacao.AgendamentoId, Erro: erro));
    }

    private static string? Normalizar(string? valor) => string.IsNullOrWhiteSpace(valor)
        ? null : string.Join(' ', valor.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class EdicaoAbortadaException(ResultadoEdicaoAgendamento resultado) : Exception
    {
        public ResultadoEdicaoAgendamento Resultado { get; } = resultado;
    }
}
