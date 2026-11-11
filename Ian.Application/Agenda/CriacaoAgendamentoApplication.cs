using Ian.Application.Catalogo;
using Ian.Application.Clientes;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public sealed class CriacaoAgendamentoApplication(
    IClientes clientes,
    IClienteStore clienteStore,
    ITipoServicoCatalogo catalogo,
    IAgendaCriacaoStore agendaStore,
    IAgendaMutationExecutor mutationExecutor) : ICriacaoAgendamento
{
    public const string OperationKey = "agendamento:create";

    public async Task<ResultadoCriacaoAgendamento> CriarAsync(
        CriarAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);
        ArgumentNullException.ThrowIfNull(solicitacao.Cliente);

        if (solicitacao.UpdateId < 0)
        {
            return Invalido(ErroCriacaoAgendamento.UpdateIdInvalido);
        }

        if (solicitacao.TipoServicoId <= 0)
        {
            return Invalido(ErroCriacaoAgendamento.TipoServicoInvalido);
        }

        var tipoServico = (await catalogo.ListarAsync(cancellationToken))
            .SingleOrDefault(tipo => tipo.Id == solicitacao.TipoServicoId);
        if (tipoServico is null)
        {
            return Invalido(ErroCriacaoAgendamento.TipoServicoNaoEncontrado);
        }

        var modalidade = solicitacao.Modalidade ?? tipoServico.ModalidadePadrao;
        if (!Enum.IsDefined(modalidade))
        {
            return Invalido(ErroCriacaoAgendamento.ModalidadeInvalida);
        }

        var unidade = solicitacao.UnidadeDuracao ?? tipoServico.UnidadeDuracaoPadrao;
        if (!Enum.IsDefined(unidade))
        {
            return Invalido(ErroCriacaoAgendamento.UnidadeDuracaoInvalida);
        }

        var duracao = solicitacao.Duracao ?? tipoServico.DuracaoPadrao;
        var calculo = CalculadoraOcupacao.Calcular(
            solicitacao.Data,
            solicitacao.Horario,
            duracao,
            unidade);
        if (calculo.Estado == EstadoCalculoOcupacao.RequerEsclarecimento)
        {
            return new(
                EstadoCriacaoAgendamento.RequerEsclarecimento,
                null,
                null,
                false,
                Array.Empty<PeriodoCalculado>(),
                null,
                calculo.Motivo);
        }

        if (calculo.Estado == EstadoCalculoOcupacao.Invalido)
        {
            return new(
                EstadoCriacaoAgendamento.DadosInvalidos,
                null,
                null,
                false,
                Array.Empty<PeriodoCalculado>(),
                ErroCriacaoAgendamento.CalculoInvalido,
                calculo.Motivo);
        }

        var enderecoInformado = NormalizarEspacos(solicitacao.EnderecoServico);
        var observacoes = NormalizarEspacos(solicitacao.Observacoes);
        ResultadoCriacaoAgendamento? confirmado = null;

        try
        {
            var executado = await mutationExecutor.ExecuteOnceAsync(
                solicitacao.UpdateId,
                OperationKey,
                async token =>
                {
                    var preparacao = await clientes.PrepararCadastroAsync(
                        solicitacao.Cliente,
                        token);
                    if (preparacao.Estado == EstadoPreparacaoCliente.Invalido ||
                        preparacao.Cliente is not { } cliente)
                    {
                        throw new CriacaoAbortadaException(
                            Invalido(ErroCriacaoAgendamento.ClienteInvalido));
                    }

                    var endereco = enderecoInformado;
                    if (modalidade == ModalidadeServico.Externo && endereco is null)
                    {
                        endereco = NormalizarEspacos(cliente.EnderecoPrincipal);
                    }

                    if (modalidade == ModalidadeServico.Externo && endereco is null)
                    {
                        throw new CriacaoAbortadaException(
                            Invalido(ErroCriacaoAgendamento.EnderecoExternoObrigatorio));
                    }

                    if (await agendaStore.ExisteConflitoAsync(calculo.Periodos, token))
                    {
                        throw new CriacaoAbortadaException(
                            new(
                                EstadoCriacaoAgendamento.Conflito,
                                null,
                                null,
                                false,
                                calculo.Periodos));
                    }

                    var clienteCriado = preparacao.Estado == EstadoPreparacaoCliente.Novo;
                    if (clienteCriado)
                    {
                        var concorrente = await clienteStore.SalvarNovoAsync(cliente, token);
                        if (concorrente is not null)
                        {
                            cliente = concorrente;
                            clienteCriado = false;
                        }
                    }

                    if (cliente.Id <= 0)
                    {
                        throw new InvalidOperationException(
                            "O cliente precisa estar persistido antes do agendamento.");
                    }

                    var agendamento = new Agendamento
                    {
                        ClienteId = cliente.Id,
                        TipoServicoId = tipoServico.Id,
                        Modalidade = modalidade,
                        Duracao = duracao,
                        UnidadeDuracao = unidade,
                        InicioPrevistoUtc = calculo.Periodos[0].InicioUtc,
                        FimPrevistoUtc = calculo.Periodos[^1].FimUtc,
                        EnderecoSnapshot = endereco,
                        Observacoes = observacoes,
                        Estado = EstadoAgendamento.Agendado
                    };

                    var identificacao = await agendaStore.CriarAsync(
                        agendamento,
                        calculo.Periodos,
                        solicitacao.UpdateId,
                        OperationKey,
                        token);
                    confirmado = new(
                        EstadoCriacaoAgendamento.Criado,
                        identificacao.AgendamentoId,
                        identificacao.ClienteId,
                        clienteCriado,
                        calculo.Periodos);
                },
                cancellationToken);

            if (!executado)
            {
                var existente = await agendaStore.ObterPorOperacaoAsync(
                    solicitacao.UpdateId,
                    OperationKey,
                    cancellationToken);
                return existente is null
                    ? new(
                        EstadoCriacaoAgendamento.Falha,
                        null,
                        null,
                        false,
                        Array.Empty<PeriodoCalculado>(),
                        ErroCriacaoAgendamento.ResultadoIdempotenteAusente)
                    : new(
                        EstadoCriacaoAgendamento.Reentrega,
                        existente.AgendamentoId,
                        existente.ClienteId,
                        false,
                        Array.Empty<PeriodoCalculado>());
            }

            return confirmado ?? throw new InvalidOperationException(
                "A operação confirmada deve produzir um agendamento.");
        }
        catch (CriacaoAbortadaException exception)
        {
            return exception.Resultado;
        }
        catch (AgendaLockUnavailableException)
        {
            return new(
                EstadoCriacaoAgendamento.AgendaIndisponivel,
                null,
                null,
                false,
                Array.Empty<PeriodoCalculado>());
        }
    }

    private static ResultadoCriacaoAgendamento Invalido(ErroCriacaoAgendamento erro) =>
        new(
            EstadoCriacaoAgendamento.DadosInvalidos,
            null,
            null,
            false,
            Array.Empty<PeriodoCalculado>(),
            erro);

    private static string? NormalizarEspacos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        return string.Join(' ', valor.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class CriacaoAbortadaException(ResultadoCriacaoAgendamento resultado)
        : Exception
    {
        public ResultadoCriacaoAgendamento Resultado { get; } = resultado;
    }
}
