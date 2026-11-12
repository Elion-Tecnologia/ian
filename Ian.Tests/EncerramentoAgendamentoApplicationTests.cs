using Ian.Application.Agenda;
using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Tests;

public sealed class EncerramentoAgendamentoApplicationTests
{
    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public async Task IdentificacaoInvalidaNaoIniciaEscrita(long updateId, long id, long versao)
    {
        var fake = new Dependencias();
        Assert.Equal(AgentToolOutcome.Conflict, (await Application(fake).ConcluirAsync(new(updateId, id, versao))).Outcome);
        Assert.False(fake.TransacaoIniciada);
        Assert.False(fake.Registrado);
    }

    [Fact]
    public async Task CancelamentoSemAceiteNaoInvocaConfirmacaoNemPersistencia()
    {
        var fake = new Dependencias();
        Assert.Equal(AgentToolOutcome.Conflict, (await Application(fake).CancelarAsync(new(1, new(123, 123, true, true, true), null))).Outcome);
        Assert.False(fake.TransacaoIniciada);
        Assert.False(fake.Registrado);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FalhasDeLockOuConcorrenciaNaoViramSucesso(bool versao)
    {
        var fake = new Dependencias { FalhaVersao = versao, FalhaLock = !versao };
        Assert.Equal(AgentToolOutcome.Conflict, (await Application(fake).ConcluirAsync(new(1, 1, 1))).Outcome);
        Assert.False(fake.Registrado);
        Assert.Equal(EstadoAgendamento.Agendado, fake.Atual.Estado);
    }

    [Theory]
    [InlineData(EstadoAgendamento.Concluido)]
    [InlineData(EstadoAgendamento.Cancelado)]
    public async Task EstadoTerminalNaoEhAlterado(EstadoAgendamento estado)
    {
        var fake = new Dependencias();
        fake.Atual.Estado = estado;
        Assert.Equal(AgentToolOutcome.Conflict, (await Application(fake).ConcluirAsync(new(1, 1, 1))).Outcome);
        Assert.Equal(estado, fake.Atual.Estado);
        Assert.Equal(1, fake.Atual.Versao);
        Assert.False(fake.Registrado);
    }

    [Fact]
    public async Task CancelamentoDaExecucaoEhPropagado()
    {
        var fake = new Dependencias();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Application(fake).ConcluirAsync(new(1, 1, 1), cancellation.Token));
        Assert.False(fake.Registrado);
    }

    private static EncerramentoAgendamentoApplication Application(Dependencias fake) => new(fake, fake, fake, TimeProvider.System);

    private sealed class Dependencias : IAgendaEncerramentoStore, IAgendaMutationExecutor, IOperationConfirmation
    {
        public bool FalhaVersao { get; init; }
        public bool FalhaLock { get; init; }
        public bool TransacaoIniciada { get; private set; }
        public bool Registrado { get; private set; }
        public Agendamento Atual { get; } = new()
        {
            Id = 1, ClienteId = 1, TipoServicoId = 1, Duracao = 60,
            Modalidade = ModalidadeServico.Interno, UnidadeDuracao = UnidadeDuracao.Minutos,
            InicioPrevistoUtc = new(2030, 1, 7, 11, 0, 0, TimeSpan.Zero),
            FimPrevistoUtc = new(2030, 1, 7, 12, 0, 0, TimeSpan.Zero)
        };
        public async Task<bool> ExecuteOnceAsync(long updateId, string operationKey, Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TransacaoIniciada = true;
            if (FalhaVersao) throw new AgendaVersionConflictException();
            if (FalhaLock) throw new AgendaLockUnavailableException();
            await mutation(cancellationToken);
            return true;
        }
        public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default)
        {
            Assert.True(TransacaoIniciada);
            return Task.FromResult<Agendamento?>(Atual);
        }
        public Task RegistrarAsync(Agendamento agendamento, long updateId, string operationKey, DateTimeOffset instanteUtc,
            CancellationToken cancellationToken = default)
        {
            Registrado = true;
            return Task.CompletedTask;
        }
        public Task<AgendamentoEncerradoItem?> ObterPorOperacaoAsync(long updateId, string operationKey,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentToolResult> PrepareAsync(AgentToolCaller caller, long pendingRequestId, AlterarAgendamentoToolRequest arguments,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Confirmação não esperada.");
        public Task<AgentToolResult> ExecuteAsync(AgentToolCaller caller, long updateId, ConfirmationAcceptance acceptance,
            Func<ConfirmedAgendaOperation, CancellationToken, Task<AgentToolResult>> mutation,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Cancelamento sem aceite não pode executar.");
    }
}
