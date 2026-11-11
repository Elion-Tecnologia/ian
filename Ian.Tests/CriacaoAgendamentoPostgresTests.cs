using Ian.Application.Agenda;
using Ian.Application.Clientes;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class CriacaoAgendamentoPostgresTests
{
    [PostgresFact]
    public async Task ClienteExistenteUsaPadroesEPersisteEstadoVersaoEPeriodo()
    {
        await InitializeAsync(1);
        await SeedClienteAsync("11999999999", "Rua Principal");
        await using var db = PostgresTestDatabase.CreateContext();

        var resultado = await Application(db).CriarAsync(Request(
            1,
            TiposServicoPredefinidos.VisitaTecnicaId,
            new TimeOnly(8, 0),
            enderecoServico: "Rua do Serviço"));

        Assert.Equal(EstadoCriacaoAgendamento.Criado, resultado.Estado);
        Assert.False(resultado.ClienteCriado);
        var agendamento = await db.Agendamentos.AsNoTracking().SingleAsync();
        Assert.Equal(240, agendamento.Duracao);
        Assert.Equal(UnidadeDuracao.Minutos, agendamento.UnidadeDuracao);
        Assert.Equal(ModalidadeServico.Externo, agendamento.Modalidade);
        Assert.Equal("Rua do Serviço", agendamento.EnderecoSnapshot);
        Assert.Equal(EstadoAgendamento.Agendado, agendamento.Estado);
        Assert.Equal(1, agendamento.Versao);
        var periodo = await db.PeriodosOcupacao.AsNoTracking().SingleAsync();
        Assert.Equal(Utc(2030, 1, 7, 11), periodo.InicioUtc);
        Assert.Equal(Utc(2030, 1, 7, 15), periodo.FimUtc);
    }

    [PostgresFact]
    public async Task ClienteNovoESeteDiasSaoConfirmadosNaMesmaTransacao()
    {
        await InitializeAsync(2);
        await using var db = PostgresTestDatabase.CreateContext();

        var resultado = await Application(db).CriarAsync(Request(
            2,
            TiposServicoPredefinidos.MontagemId,
            null,
            enderecoPrincipal: "Rua Nova"));

        Assert.Equal(EstadoCriacaoAgendamento.Criado, resultado.Estado);
        Assert.True(resultado.ClienteCriado);
        Assert.Equal(7, resultado.Periodos.Count);
        Assert.Equal(1, await db.Clientes.CountAsync());
        Assert.Equal(1, await db.Agendamentos.CountAsync());
        Assert.Equal(7, await db.PeriodosOcupacao.CountAsync());
        var agendamento = await db.Agendamentos.AsNoTracking().SingleAsync();
        Assert.Equal("Rua Nova", agendamento.EnderecoSnapshot);
        Assert.Equal(Utc(2030, 1, 7, 11), agendamento.InicioPrevistoUtc);
        Assert.Equal(Utc(2030, 1, 13, 21), agendamento.FimPrevistoUtc);
    }

    [PostgresFact]
    public async Task SobrescritaDeDuracaoEModalidadeNaoAlteraCatalogo()
    {
        await InitializeAsync(3);
        await SeedClienteAsync("11999999999", null);
        await using var db = PostgresTestDatabase.CreateContext();
        var solicitacao = Request(
            3,
            TiposServicoPredefinidos.VisitaTecnicaId,
            new TimeOnly(16, 0)) with
        {
            Modalidade = ModalidadeServico.Interno,
            Duracao = 240
        };

        var resultado = await Application(db).CriarAsync(solicitacao);

        Assert.Equal(EstadoCriacaoAgendamento.Criado, resultado.Estado);
        Assert.Equal(2, resultado.Periodos.Count);
        var agendamento = await db.Agendamentos.AsNoTracking().SingleAsync();
        Assert.Equal(ModalidadeServico.Interno, agendamento.Modalidade);
        Assert.Null(agendamento.EnderecoSnapshot);
        var tipo = await db.TiposServico.AsNoTracking().SingleAsync(
            item => item.Id == TiposServicoPredefinidos.VisitaTecnicaId);
        Assert.Equal(ModalidadeServico.Externo, tipo.ModalidadePadrao);
        Assert.Equal(240, tipo.DuracaoPadrao);
    }

    [PostgresFact]
    public async Task EnderecoExternoHorarioInvalidoEEsclarecimentoNaoGravamNada()
    {
        await InitializeAsync(4, 5, 6, 14);
        await SeedClienteAsync("11999999999", null);
        await using var db = PostgresTestDatabase.CreateContext();
        var application = Application(db);

        var semEndereco = await application.CriarAsync(Request(
            4,
            TiposServicoPredefinidos.VisitaTecnicaId,
            new TimeOnly(8, 0)));
        var foraExpediente = await application.CriarAsync(Request(
            5,
            TiposServicoPredefinidos.VisitaTecnicaId,
            new TimeOnly(18, 0),
            enderecoServico: "Rua A"));
        var esclarecimento = await application.CriarAsync(Request(
            6,
            TiposServicoPredefinidos.MontagemId,
            new TimeOnly(10, 0),
            enderecoServico: "Rua A"));
        var clienteInvalido = await application.CriarAsync(Request(
            14,
            TiposServicoPredefinidos.OrcamentoId,
            null) with
        {
            Cliente = new(" ", "telefone")
        });

        Assert.Equal(ErroCriacaoAgendamento.EnderecoExternoObrigatorio, semEndereco.Erro);
        Assert.Equal(MotivoCalculoOcupacao.InicioForaDoExpediente, foraExpediente.MotivoCalculo);
        Assert.Equal(EstadoCriacaoAgendamento.RequerEsclarecimento, esclarecimento.Estado);
        Assert.Equal(ErroCriacaoAgendamento.ClienteInvalido, clienteInvalido.Erro);
        Assert.Equal(0, await db.Agendamentos.CountAsync());
        Assert.Equal(0, await db.PeriodosOcupacao.CountAsync());
        Assert.Equal(0, await db.TelegramBusinessOperations.CountAsync());
    }

    [PostgresFact]
    public async Task ConflitoEhRejeitadoEPeriodoAdjacenteEhPermitido()
    {
        await InitializeAsync(7, 8, 9);
        await SeedClienteAsync("11999999999", null);
        await using var db = PostgresTestDatabase.CreateContext();
        var application = Application(db);

        var primeiro = await application.CriarAsync(Interno(7, new TimeOnly(8, 0), 60));
        var conflito = await application.CriarAsync(
            Interno(8, new TimeOnly(8, 30), 60) with
            {
                Cliente = new("Cliente novo", "21999999999")
            });
        var adjacente = await application.CriarAsync(Interno(9, new TimeOnly(9, 0), 60));

        Assert.Equal(EstadoCriacaoAgendamento.Criado, primeiro.Estado);
        Assert.Equal(EstadoCriacaoAgendamento.Conflito, conflito.Estado);
        Assert.Equal(EstadoCriacaoAgendamento.Criado, adjacente.Estado);
        Assert.Equal(2, await db.Agendamentos.CountAsync());
        Assert.Equal(2, await db.PeriodosOcupacao.CountAsync());
        Assert.Equal(1, await db.Clientes.CountAsync());
        Assert.Equal(0, await db.TelegramBusinessOperations.CountAsync(
            operation => operation.UpdateId == 8));
    }

    [PostgresFact]
    public async Task DuasCriacoesConcorrentesNoMesmoHorarioConfirmamSomenteUma()
    {
        await InitializeAsync(10, 11);
        await SeedClienteAsync("11999999999", null);
        await using var firstDb = PostgresTestDatabase.CreateContext();
        await using var secondDb = PostgresTestDatabase.CreateContext();

        var resultados = await Task.WhenAll(
            Application(firstDb).CriarAsync(Interno(10, new TimeOnly(8, 0), 60)),
            Application(secondDb).CriarAsync(Interno(11, new TimeOnly(8, 0), 60)));

        Assert.Single(resultados, item => item.Estado == EstadoCriacaoAgendamento.Criado);
        Assert.Single(resultados, item => item.Estado == EstadoCriacaoAgendamento.Conflito);
        await using var verification = PostgresTestDatabase.CreateContext();
        Assert.Equal(1, await verification.Agendamentos.CountAsync());
        Assert.Equal(1, await verification.PeriodosOcupacao.CountAsync());
        Assert.Equal(1, await verification.TelegramBusinessOperations.CountAsync());
    }

    [PostgresFact]
    public async Task ReentregaRecuperaMesmoAgendamentoSemDuplicar()
    {
        await InitializeAsync(12);
        await SeedClienteAsync("11999999999", null);
        await using var firstDb = PostgresTestDatabase.CreateContext();
        var primeiro = await Application(firstDb).CriarAsync(
            Interno(12, new TimeOnly(8, 0), 60));
        await using var secondDb = PostgresTestDatabase.CreateContext();

        var reentrega = await Application(secondDb).CriarAsync(
            Interno(12, new TimeOnly(8, 0), 60));

        Assert.Equal(EstadoCriacaoAgendamento.Criado, primeiro.Estado);
        Assert.Equal(EstadoCriacaoAgendamento.Reentrega, reentrega.Estado);
        Assert.Equal(primeiro.AgendamentoId, reentrega.AgendamentoId);
        Assert.Equal(primeiro.ClienteId, reentrega.ClienteId);
        Assert.Equal(1, await secondDb.Agendamentos.CountAsync());
        Assert.Equal(1, await secondDb.PeriodosOcupacao.CountAsync());
        Assert.Equal(
            primeiro.AgendamentoId,
            await secondDb.TelegramBusinessOperations
                .Where(operation => operation.UpdateId == 12)
                .Select(operation => operation.AgendamentoId)
                .SingleAsync());
    }

    [PostgresFact]
    public async Task FalhaDepoisDasGravacoesIntermediariasReverteClienteAgendamentoEPeriodos()
    {
        await InitializeAsync(13);
        await using var db = PostgresTestDatabase.CreateContext();
        var clienteStore = new PostgresClienteStore(db);
        var application = new CriacaoAgendamentoApplication(
            new ClientesApplication(clienteStore),
            clienteStore,
            new PostgresTipoServicoCatalogo(db),
            new FailingAgendaStore(db),
            new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)));

        await Assert.ThrowsAsync<SyntheticFailureException>(() => application.CriarAsync(
            Interno(13, new TimeOnly(8, 0), 60)));

        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(0, await db.Clientes.CountAsync());
        Assert.Equal(0, await db.Agendamentos.CountAsync());
        Assert.Equal(0, await db.PeriodosOcupacao.CountAsync());
        Assert.Equal(0, await db.TelegramBusinessOperations.CountAsync());
    }

    private static CriacaoAgendamentoApplication Application(IanDbContext db)
    {
        var clienteStore = new PostgresClienteStore(db);
        return new(
            new ClientesApplication(clienteStore),
            clienteStore,
            new PostgresTipoServicoCatalogo(db),
            new PostgresAgendaCriacaoStore(db),
            new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)));
    }

    private static CriarAgendamentoSolicitacao Request(
        long updateId,
        long tipoServicoId,
        TimeOnly? horario,
        string? enderecoPrincipal = null,
        string? enderecoServico = null) =>
        new(
            updateId,
            new("Maria", "11999999999", enderecoPrincipal),
            tipoServicoId,
            new DateOnly(2030, 1, 7),
            horario,
            EnderecoServico: enderecoServico);

    private static CriarAgendamentoSolicitacao Interno(
        long updateId,
        TimeOnly horario,
        int duracao) =>
        Request(updateId, TiposServicoPredefinidos.VisitaTecnicaId, horario) with
        {
            Modalidade = ModalidadeServico.Interno,
            Duracao = duracao
        };

    private static async Task InitializeAsync(params long[] updateIds)
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
        db.TelegramUpdatesInbox.AddRange(updateIds.Select(updateId => new TelegramUpdateInbox
        {
            UpdateId = updateId,
            PayloadJson = $$"""{"update_id":{{updateId}}}""",
            Estado = EstadoTelegramUpdate.Received,
            RecebidoEmUtc = Utc(2030, 1, 1, 12)
        }));
        await db.SaveChangesAsync();
    }

    private static async Task SeedClienteAsync(string telefone, string? endereco)
    {
        await using var db = PostgresTestDatabase.CreateContext();
        db.Clientes.Add(new Cliente
        {
            Nome = "Maria",
            TelefoneNormalizado = telefone,
            EnderecoPrincipal = endereco
        });
        await db.SaveChangesAsync();
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private sealed class FailingAgendaStore(IanDbContext context) : IAgendaCriacaoStore
    {
        public Task<bool> ExisteConflitoAsync(
            IReadOnlyList<PeriodoCalculado> periodos,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public async Task<IdentificacaoAgendamento> CriarAsync(
            Agendamento agendamento,
            IReadOnlyList<PeriodoCalculado> periodos,
            long updateId,
            string operationKey,
            CancellationToken cancellationToken = default)
        {
            context.Agendamentos.Add(agendamento);
            await context.SaveChangesAsync(cancellationToken);
            context.PeriodosOcupacao.AddRange(periodos.Select(periodo => new PeriodoOcupacao
            {
                AgendamentoId = agendamento.Id,
                InicioUtc = periodo.InicioUtc,
                FimUtc = periodo.FimUtc
            }));
            await context.SaveChangesAsync(cancellationToken);
            throw new SyntheticFailureException();
        }

        public Task<IdentificacaoAgendamento?> ObterPorOperacaoAsync(
            long updateId,
            string operationKey,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SyntheticFailureException : Exception;
}
