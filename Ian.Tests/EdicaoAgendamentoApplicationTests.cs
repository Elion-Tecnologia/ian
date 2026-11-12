using Ian.Application.Agenda;
using Ian.Application.Catalogo;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Tests;

public sealed class EdicaoAgendamentoApplicationTests
{
    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public async Task IdentificacaoInvalidaNaoIniciaTransacao(long update, long id, long versao)
    {
        var fake = new Dependencias();
        var resultado = await Application(fake).EditarAsync(new(update, id, versao));
        Assert.Equal(ErroEdicaoAgendamento.IdentificacaoInvalida, resultado.Erro);
        Assert.False(fake.TransacaoIniciada);
    }

    [Fact]
    public async Task LockIndisponivelRetornaFalhaSemCarregarOuAlterarAgendamento()
    {
        var fake = new Dependencias { LockIndisponivel = true };
        Assert.Equal(EstadoEdicaoAgendamento.AgendaIndisponivel,
            (await Application(fake).EditarAsync(new(1, 1, 1))).Estado);
        Assert.False(fake.Carregado);
        Assert.False(fake.Substituido);
    }

    [Fact]
    public async Task ConflitoEfEhTraduzidoParaVersaoObsoleta()
    {
        var fake = new Dependencias { ConflitoVersao = true };
        Assert.Equal(EstadoEdicaoAgendamento.VersaoObsoleta,
            (await Application(fake).EditarAsync(new(1, 1, 1))).Estado);
        Assert.False(fake.Carregado);
    }

    [Fact]
    public async Task ReentregaRetornaIdentidadeOriginalSemExecutarEdicao()
    {
        var fake = new Dependencias { Reentrega = true };
        var resultado = await Application(fake).EditarAsync(new(1, 999, 1));
        Assert.Equal(EstadoEdicaoAgendamento.Reentrega, resultado.Estado);
        Assert.Equal(1, resultado.AgendamentoId);
        Assert.Null(resultado.Versao);
        Assert.False(fake.Carregado);
    }

    [Fact]
    public async Task ConversaoParaDiasNaoCorrigeSilenciosamenteInicioAtualDivergente()
    {
        var fake = new Dependencias();
        var resultado = await Application(fake).EditarAsync(new(1, 1, 1,
            Duracao: 2, UnidadeDuracao: UnidadeDuracao.DiasCorridos));
        Assert.Equal(EstadoEdicaoAgendamento.RequerEsclarecimento, resultado.Estado);
        Assert.False(fake.Substituido);
        Assert.Equal(1, fake.Atual.Versao);
        Assert.Equal(UnidadeDuracao.Minutos, fake.Atual.UnidadeDuracao);
    }

    [Fact]
    public async Task CancelamentoDeExecucaoEhPropagadoSemDeclararSucesso()
    {
        var fake = new Dependencias();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Application(fake).EditarAsync(new(1, 1, 1), cancellation.Token));
        Assert.False(fake.Substituido);
    }

    private static EdicaoAgendamentoApplication Application(Dependencias fake) => new(fake, fake, fake, fake);

    private sealed class Dependencias : IAgendaMutationExecutor, IAgendaEdicaoStore, IConsultaAgendaStore, ITipoServicoCatalogo
    {
        public bool LockIndisponivel { get; init; }
        public bool ConflitoVersao { get; init; }
        public bool Reentrega { get; init; }
        public bool TransacaoIniciada { get; private set; }
        public bool Carregado { get; private set; }
        public bool Substituido { get; private set; }
        public Agendamento Atual { get; } = new()
        {
            Id = 1, ClienteId = 1, TipoServicoId = 1, Duracao = 60,
            Modalidade = ModalidadeServico.Interno, UnidadeDuracao = UnidadeDuracao.Minutos,
            InicioPrevistoUtc = new(2030, 1, 7, 13, 0, 0, TimeSpan.Zero),
            FimPrevistoUtc = new(2030, 1, 7, 14, 0, 0, TimeSpan.Zero)
        };

        public async Task<bool> ExecuteOnceAsync(long updateId, string operationKey,
            Func<CancellationToken, Task> mutation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TransacaoIniciada = true;
            if (LockIndisponivel) throw new AgendaLockUnavailableException();
            if (ConflitoVersao) throw new AgendaVersionConflictException();
            if (Reentrega) return false;
            await mutation(cancellationToken);
            return true;
        }
        public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default)
        {
            Assert.True(TransacaoIniciada);
            Carregado = true;
            return Task.FromResult<Agendamento?>(Atual);
        }
        public Task<long?> ObterIdPorOperacaoAsync(long updateId, CancellationToken cancellationToken = default) => Task.FromResult<long?>(1);
        public Task SubstituirPeriodosAsync(Agendamento agendamento, IReadOnlyList<PeriodoCalculado> periodos,
            long updateId, CancellationToken cancellationToken = default)
        {
            Substituido = true;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<TipoServicoCatalogoItem>> ListarAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TipoServicoCatalogoItem>>([new(1, "Serviço", 60, UnidadeDuracao.Minutos, ModalidadeServico.Interno)]);
        public Task<IReadOnlyList<long>> ObterConflitosAsync(IReadOnlyList<PeriodoAgendaStoreItem> periodos,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<long>>([]);
        public Task<IReadOnlyList<AgendamentoAgendaStoreItem>> ConsultarAsync(ConsultaAgendaStoreFiltro filtro,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
