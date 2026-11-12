using Ian.Application.Agenda;
using Ian.Application.Catalogo;
using Ian.Application.Clientes;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Tests;

public sealed class CriacaoAgendamentoApplicationTests
{
    [Fact]
    public async Task HorarioDivergenteEmDiasCorridosPedeEsclarecimentoSemMutacao()
    {
        var executor = new ExecutorFake();
        var application = CreateApplication(executor: executor);

        var resultado = await application.CriarAsync(new(
            1,
            new("Maria", "11999999999"),
            TiposServicoPredefinidos.MontagemId,
            new DateOnly(2030, 1, 7),
            new TimeOnly(10, 0)));

        Assert.Equal(EstadoCriacaoAgendamento.RequerEsclarecimento, resultado.Estado);
        Assert.Equal(MotivoCalculoOcupacao.HorarioDiasCorridosDivergente, resultado.MotivoCalculo);
        Assert.Equal(0, executor.Execucoes);
    }

    [Fact]
    public async Task UsaPadroesDoCatalogoEEnderecoPrincipalDoCliente()
    {
        var store = new AgendaStoreFake();
        var application = CreateApplication(agendaStore: store);

        var resultado = await application.CriarAsync(new(
            1,
            new("Maria", "11999999999"),
            TiposServicoPredefinidos.VisitaTecnicaId,
            new DateOnly(2030, 1, 7),
            new TimeOnly(8, 0)));

        Assert.Equal(EstadoCriacaoAgendamento.Criado, resultado.Estado);
        Assert.NotNull(store.Agendamento);
        Assert.Equal(240, store.Agendamento.Duracao);
        Assert.Equal(UnidadeDuracao.Minutos, store.Agendamento.UnidadeDuracao);
        Assert.Equal(ModalidadeServico.Externo, store.Agendamento.Modalidade);
        Assert.Equal("Rua Principal", store.Agendamento.EnderecoSnapshot);
        Assert.Single(store.Periodos);
    }

    [Fact]
    public async Task SobrescreveDuracaoEModalidadeSemAlterarCatalogo()
    {
        var store = new AgendaStoreFake();
        var application = CreateApplication(agendaStore: store);

        var resultado = await application.CriarAsync(new(
            1,
            new("Maria", "11999999999"),
            TiposServicoPredefinidos.VisitaTecnicaId,
            new DateOnly(2030, 1, 7),
            new TimeOnly(8, 0),
            ModalidadeServico.Interno,
            60));

        Assert.Equal(EstadoCriacaoAgendamento.Criado, resultado.Estado);
        Assert.Equal(60, store.Agendamento?.Duracao);
        Assert.Equal(ModalidadeServico.Interno, store.Agendamento?.Modalidade);
        Assert.Null(store.Agendamento?.EnderecoSnapshot);
    }

    [Fact]
    public async Task ExternoSemEnderecoEhInvalidoENaoCriaAgendamento()
    {
        var store = new AgendaStoreFake();
        var application = CreateApplication(
            agendaStore: store,
            cliente: new Cliente
            {
                Id = 10,
                Nome = "Maria",
                TelefoneNormalizado = "11999999999"
            });

        var resultado = await application.CriarAsync(new(
            1,
            new("Maria", "11999999999"),
            TiposServicoPredefinidos.VisitaTecnicaId,
            new DateOnly(2030, 1, 7),
            new TimeOnly(8, 0)));

        Assert.Equal(EstadoCriacaoAgendamento.DadosInvalidos, resultado.Estado);
        Assert.Equal(ErroCriacaoAgendamento.EnderecoExternoObrigatorio, resultado.Erro);
        Assert.Null(store.Agendamento);
    }

    [Fact]
    public async Task LockIndisponivelRetornaFalhaTipadaSemSucessoPresumido()
    {
        var application = CreateApplication(
            executor: new ExecutorFake { LockIndisponivel = true });

        var resultado = await application.CriarAsync(new(
            1,
            new("Maria", "11999999999"),
            TiposServicoPredefinidos.OrcamentoId,
            new DateOnly(2030, 1, 7),
            null));

        Assert.Equal(EstadoCriacaoAgendamento.AgendaIndisponivel, resultado.Estado);
        Assert.Null(resultado.AgendamentoId);
    }

    [Fact]
    public async Task DadosInvalidosNaoIniciamMutacao()
    {
        var executor = new ExecutorFake();
        var application = CreateApplication(executor: executor);
        var baseRequest = new CriarAgendamentoSolicitacao(
            1,
            new("Maria", "11999999999"),
            TiposServicoPredefinidos.VisitaTecnicaId,
            new DateOnly(2030, 1, 7),
            new TimeOnly(8, 0),
            EnderecoServico: "Rua A");

        var resultados = new[]
        {
            await application.CriarAsync(baseRequest with { UpdateId = -1 }),
            await application.CriarAsync(baseRequest with { TipoServicoId = 0 }),
            await application.CriarAsync(baseRequest with { TipoServicoId = 999 }),
            await application.CriarAsync(baseRequest with { Modalidade = (ModalidadeServico)999 }),
            await application.CriarAsync(baseRequest with { UnidadeDuracao = (UnidadeDuracao)999 }),
            await application.CriarAsync(baseRequest with { Duracao = 0 })
        };

        Assert.All(resultados, resultado =>
            Assert.Equal(EstadoCriacaoAgendamento.DadosInvalidos, resultado.Estado));
        Assert.Equal(0, executor.Execucoes);
    }

    private static CriacaoAgendamentoApplication CreateApplication(
        ExecutorFake? executor = null,
        AgendaStoreFake? agendaStore = null,
        Cliente? cliente = null)
    {
        cliente ??= new Cliente
        {
            Id = 10,
            Nome = "Maria",
            TelefoneNormalizado = "11999999999",
            EnderecoPrincipal = "Rua Principal"
        };
        var clienteStore = new ClienteStoreFake();
        return new(
            new ClientesFake(cliente),
            clienteStore,
            new CatalogoFake(),
            agendaStore ?? new AgendaStoreFake(),
            executor ?? new ExecutorFake());
    }

    private sealed class ClientesFake(Cliente cliente) : IClientes
    {
        public Task<ResultadoLocalizacaoCliente> LocalizarPorIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreparacaoCadastroCliente> PrepararCadastroAsync(
            CadastrarClienteSolicitacao solicitacao,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PreparacaoCadastroCliente(
                EstadoPreparacaoCliente.Existente,
                cliente,
                null));

        public Task<ResultadoCadastroCliente> CadastrarAsync(
            CadastrarClienteSolicitacao solicitacao,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResultadoLocalizacaoCliente> LocalizarPorTelefoneAsync(
            string? telefone,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResultadoLocalizacaoCliente> LocalizarPorNomeAsync(
            string? nome,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ClienteStoreFake : IClienteStore
    {
        public Task<Cliente?> LocalizarPorIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Cliente?> LocalizarParaCadastroAsync(
            string telefoneNormalizado,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Cliente?> LocalizarPorTelefoneAsync(
            string telefoneNormalizado,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Cliente>> LocalizarPorNomeAsync(
            string nome,
            int limite,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Adicionar(Cliente cliente) => throw new NotSupportedException();

        public Task<Cliente?> SalvarNovoAsync(
            Cliente cliente,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Cliente?>(null);
    }

    private sealed class CatalogoFake : ITipoServicoCatalogo
    {
        public Task<IReadOnlyList<TipoServicoCatalogoItem>> ListarAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TipoServicoCatalogoItem>>(
            [
                new(
                    TiposServicoPredefinidos.VisitaTecnicaId,
                    TiposServicoPredefinidos.VisitaTecnicaNome,
                    240,
                    UnidadeDuracao.Minutos,
                    ModalidadeServico.Externo),
                new(
                    TiposServicoPredefinidos.MontagemId,
                    TiposServicoPredefinidos.MontagemNome,
                    7,
                    UnidadeDuracao.DiasCorridos,
                    ModalidadeServico.Externo),
                new(
                    TiposServicoPredefinidos.OrcamentoId,
                    TiposServicoPredefinidos.OrcamentoNome,
                    1,
                    UnidadeDuracao.DiasCorridos,
                    ModalidadeServico.Interno)
            ]);
    }

    private sealed class AgendaStoreFake : IAgendaCriacaoStore
    {
        public Agendamento? Agendamento { get; private set; }
        public IReadOnlyList<PeriodoCalculado> Periodos { get; private set; } = [];

        public Task<bool> ExisteConflitoAsync(
            IReadOnlyList<PeriodoCalculado> periodos,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IdentificacaoAgendamento> CriarAsync(
            Agendamento agendamento,
            IReadOnlyList<PeriodoCalculado> periodos,
            long updateId,
            string operationKey,
            CancellationToken cancellationToken = default)
        {
            Agendamento = agendamento;
            Periodos = periodos;
            return Task.FromResult(new IdentificacaoAgendamento(100, agendamento.ClienteId));
        }

        public Task<IdentificacaoAgendamento?> ObterPorOperacaoAsync(
            long updateId,
            string operationKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentificacaoAgendamento?>(new(100, 10));
    }

    private sealed class ExecutorFake : IAgendaMutationExecutor
    {
        public int Execucoes { get; private set; }
        public bool LockIndisponivel { get; init; }

        public async Task<bool> ExecuteOnceAsync(
            long updateId,
            string operationKey,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken = default)
        {
            Execucoes++;
            if (LockIndisponivel)
            {
                throw new AgendaLockUnavailableException();
            }

            await mutation(cancellationToken);
            return true;
        }
    }
}
