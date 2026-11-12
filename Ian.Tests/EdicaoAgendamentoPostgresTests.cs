using Ian.Application.Agenda;
using Ian.Application.Clientes;
using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class EdicaoAgendamentoPostgresTests
{
    private static readonly DateOnly Data = new(2030, 1, 7);

    [PostgresFact]
    public async Task ReagendaEEditaCamposPreservandoIdentidadeClienteECatalogo()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var resultado = await Application(db).EditarAsync(new(2, id, 1,
            Data: Data.AddDays(1), Horario: new(16, 0), Duracao: 240,
            Modalidade: ModalidadeServico.Remoto, EnderecoServico: " Rua  Nova ", Observacoes: " nota "));

        Assert.Equal(EstadoEdicaoAgendamento.Editado, resultado.Estado);
        Assert.Equal(2, resultado.Versao);
        Assert.Equal(2, resultado.Periodos?.Count);
        var atual = await db.Agendamentos.AsNoTracking().SingleAsync();
        Assert.Equal(id, atual.Id);
        Assert.Equal(EstadoAgendamento.Agendado, atual.Estado);
        Assert.Equal(ModalidadeServico.Remoto, atual.Modalidade);
        Assert.Equal(240, atual.Duracao);
        Assert.Equal("Rua Nova", atual.EnderecoSnapshot);
        Assert.Equal("nota", atual.Observacoes);
        Assert.Equal(2, await db.PeriodosOcupacao.CountAsync());
        Assert.Equal(1, await db.Clientes.CountAsync());
        Assert.Equal(1, (await db.TiposServico.SingleAsync(t => t.Id == TiposServicoPredefinidos.OrcamentoId)).DuracaoPadrao);
        Assert.Equal(id, (await db.TelegramBusinessOperations.SingleAsync(o => o.UpdateId == 2)).AgendamentoId);
        Assert.Equal("Editado", (await db.RegistrosAuditoria.SingleAsync()).Resultado);
        var consulta = await new ConsultaAgendaApplication(new PostgresConsultaAgendaStore(db))
            .ConsultarAsync(new(AgendamentoId: id));
        Assert.Equal(2, Assert.Single(consulta.Agendamentos).Versao);
    }

    [PostgresFact]
    public async Task ConflitoPreservaTodosOsDadosEPeriodoAdjacenteEhPermitido()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await Criacao(db).CriarAsync(Criar(2, new(10, 0)));
        var antes = await SnapshotAsync(db, id);
        var recusado = await Application(db).EditarAsync(new(3, id, 1,
            Horario: new(10, 30), EnderecoServico: "Outro endereço", Observacoes: "Alterada"));
        Assert.Equal(EstadoEdicaoAgendamento.Conflito, recusado.Estado);
        Assert.Equal(antes, await SnapshotAsync(db, id));
        Assert.False(await db.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 3));
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
        var permitido = await Application(db).EditarAsync(new(4, id, 1, Horario: new(11, 0)));
        Assert.Equal(EstadoEdicaoAgendamento.Editado, permitido.Estado);
    }

    [PostgresFact]
    public async Task SnapshotSoMudaExplicitamenteEReentregaNaoIncrementaVersao()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Clientes.ExecuteUpdateAsync(s => s.SetProperty(c => c.EnderecoPrincipal, "Cadastro atualizado"));
        var app = Application(db);
        var request = new EditarAgendamentoSolicitacao(2, id, 1, Horario: new(8, 30));
        Assert.Equal(EstadoEdicaoAgendamento.Editado, (await app.EditarAsync(request)).Estado);
        Assert.Equal("Endereço original", (await db.Agendamentos.AsNoTracking().SingleAsync()).EnderecoSnapshot);
        Assert.Equal(EstadoEdicaoAgendamento.Reentrega, (await app.EditarAsync(request)).Estado);
        Assert.Equal(EstadoEdicaoAgendamento.VersaoObsoleta, (await app.EditarAsync(request with { UpdateId = 3 })).Estado);
        Assert.Equal(2, (await db.Agendamentos.AsNoTracking().SingleAsync()).Versao);
        Assert.Single(await db.RegistrosAuditoria.ToListAsync());
        var editado = await app.EditarAsync(new(4, id, 2, AlterarEndereco: true, AlterarObservacoes: true));
        Assert.Equal(EstadoEdicaoAgendamento.Editado, editado.Estado);
        var atual = await db.Agendamentos.AsNoTracking().SingleAsync();
        Assert.Null(atual.EnderecoSnapshot);
        Assert.Null(atual.Observacoes);
    }

    [PostgresFact]
    public async Task DiasCorridosExigeEsclarecimentoESempreReservaJanelasCompletas()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var antes = await SnapshotAsync(db, id);
        var request = new EditarAgendamentoSolicitacao(2, id, 1,
            UnidadeDuracao: UnidadeDuracao.DiasCorridos, Duracao: 7, Horario: new(10, 0));
        Assert.Equal(EstadoEdicaoAgendamento.RequerEsclarecimento, (await Application(db).EditarAsync(request)).Estado);
        Assert.Equal(antes, await SnapshotAsync(db, id));
        Assert.False(await db.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 2));
        var sucesso = await Application(db).EditarAsync(request with { Horario = null });
        Assert.Equal(EstadoEdicaoAgendamento.Editado, sucesso.Estado);
        Assert.Equal(7, sucesso.Periodos?.Count);
        Assert.All(sucesso.Periodos ?? [], periodo =>
        {
            Assert.Equal(11, periodo.InicioUtc.Hour);
            Assert.Equal(21, periodo.FimUtc.Hour);
        });
        Assert.Equal(new DateOnly(2030, 1, 13), DateOnly.FromDateTime(Assert.IsAssignableFrom<IReadOnlyList<Ian.Domain.Agenda.PeriodoCalculado>>(sucesso.Periodos)[^1].InicioUtc.DateTime));
    }

    [PostgresFact]
    public async Task DadosInvalidosENaoAgendadosNuncaAlteramAgenda()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var app = Application(db);
        var antes = await SnapshotAsync(db, id);
        EditarAgendamentoSolicitacao[] invalidos =
        [
            new(2, id, 1, Duracao: 0),
            new(2, id, 1, Horario: new(18, 0)),
            new(2, id, 1, TipoServicoId: -1),
            new(2, id, 1, Modalidade: (ModalidadeServico)999),
            new(2, id, 1, UnidadeDuracao: (UnidadeDuracao)999),
            new(2, id, 1, Modalidade: ModalidadeServico.Externo, AlterarEndereco: true),
            new(2, id, 0),
            new(-1, id, 1)
        ];
        foreach (var request in invalidos)
        {
            Assert.Equal(EstadoEdicaoAgendamento.DadosInvalidos, (await app.EditarAsync(request)).Estado);
            Assert.Equal(antes, await SnapshotAsync(db, id));
        }
        Assert.Equal(EstadoEdicaoAgendamento.NaoEncontrado, (await app.EditarAsync(new(2, long.MaxValue, 1))).Estado);
        foreach (var estado in new[] { EstadoAgendamento.Concluido, EstadoAgendamento.Cancelado })
        {
            await db.Agendamentos.ExecuteUpdateAsync(s => s.SetProperty(a => a.Estado, estado));
            antes = await SnapshotAsync(db, id);
            Assert.Equal(EstadoEdicaoAgendamento.EstadoNaoPermitido, (await app.EditarAsync(new(2, id, 1))).Estado);
            Assert.Equal(antes, await SnapshotAsync(db, id));
        }
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task DuasEdicoesConcorrentesDaMesmaVersaoSoConfirmamUma()
    {
        var id = await InitializeAsync();
        await using var primeiro = PostgresTestDatabase.CreateContext();
        await using var segundo = PostgresTestDatabase.CreateContext();
        // A stale tracked entity must also be discarded by the executor before reading under lock.
        await segundo.Agendamentos.SingleAsync();
        var resultados = await Task.WhenAll(
            Application(primeiro).EditarAsync(new(2, id, 1, Horario: new(10, 0))),
            Application(segundo).EditarAsync(new(3, id, 1, Horario: new(11, 0))));
        Assert.Single(resultados, r => r.Estado == EstadoEdicaoAgendamento.Editado);
        Assert.Single(resultados, r => r.Estado == EstadoEdicaoAgendamento.VersaoObsoleta);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, (await check.Agendamentos.SingleAsync()).Versao);
        Assert.Single(await check.PeriodosOcupacao.ToListAsync());
        Assert.Single(await check.RegistrosAuditoria.ToListAsync());
        Assert.Equal(2, await check.TelegramBusinessOperations.CountAsync());
    }

    [PostgresFact]
    public async Task EdicoesConcorrentesDeCompromissosDistintosRevalidamConflitos()
    {
        var id = await InitializeAsync();
        await using var primeiro = PostgresTestDatabase.CreateContext();
        var outro = await Criacao(primeiro).CriarAsync(Criar(2, new(10, 0)));
        await using var segundo = PostgresTestDatabase.CreateContext();
        var resultados = await Task.WhenAll(
            Application(primeiro).EditarAsync(new(3, id, 1, Horario: new(14, 0))),
            Application(segundo).EditarAsync(new(4, Assert.IsType<long>(outro.AgendamentoId), 1, Horario: new(14, 0))));
        Assert.Single(resultados, r => r.Estado == EstadoEdicaoAgendamento.Editado);
        Assert.Single(resultados, r => r.Estado == EstadoEdicaoAgendamento.Conflito);
        Assert.Equal(2, await primeiro.PeriodosOcupacao.CountAsync());
    }

    [PostgresFact]
    public async Task FalhaDepoisDeGravarSubstituicaoReverteAgendaPeriodosAuditoriaEIdempotencia()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var antes = await SnapshotAsync(db, id);
        var app = new EdicaoAgendamentoApplication(new FailingStore(db),
            new PostgresConsultaAgendaStore(db), new PostgresTipoServicoCatalogo(db),
            new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)));
        await Assert.ThrowsAsync<SyntheticFailureException>(() => app.EditarAsync(new(2, id, 1, Horario: new(10, 0))));
        Assert.Equal(antes, await SnapshotAsync(db, id));
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
        Assert.False(await db.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 2));
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(EstadoEdicaoAgendamento.Editado, (await Application(db).EditarAsync(new(2, id, 1, Horario: new(10, 0)))).Estado);
    }

    [PostgresFact]
    public async Task BuscaZeroUmNReutilizaSelecaoExplicitaPersistidaSemEditar()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var conversa = new PendingConversationService(new PostgresPendingConversationRepository(db, Options.Create(new TelegramOptions { AdministratorUserId = 123 })));
        var selecao = new SelecaoEdicaoAgendamentoApplication(new ConsultaAgendaApplication(new PostgresConsultaAgendaStore(db)), conversa);
        var agora = DateTimeOffset.UtcNow;
        Assert.Equal(EstadoSelecaoEdicao.Nenhum, (await selecao.BuscarAsync(123, new(AgendamentoId: long.MaxValue), agora)).Estado);
        var unico = await selecao.BuscarAsync(123, new(AgendamentoId: id), agora);
        Assert.Equal(EstadoSelecaoEdicao.Unico, unico.Estado);
        Assert.Equal(1, Assert.Single(unico.Candidatos).Versao);
        await Criacao(db).CriarAsync(Criar(2, new(10, 0)));
        var multiplo = await selecao.BuscarAsync(123, new(), agora);
        Assert.Equal(EstadoSelecaoEdicao.RequerSelecao, multiplo.Estado);
        Assert.Equal(2, multiplo.Candidatos.Count);
        Assert.Equal(PendingIntentUpdateStatus.InvalidCandidate,
            await conversa.SelectCandidateAsync(123, SelecaoEdicaoAgendamentoApplication.CampoAgendamento, long.MaxValue, agora));
        Assert.Equal(PendingIntentUpdateStatus.Updated,
            await conversa.SelectCandidateAsync(123, SelecaoEdicaoAgendamentoApplication.CampoAgendamento, id, agora));
        var estado = await conversa.GetForTurnAsync(123, agora);
        Assert.Equal(id.ToString(), estado?.CollectedFields["AgendamentoId"]);
        Assert.Equal(EstadoSelecaoEdicao.ConversaEmAndamento, (await selecao.BuscarAsync(123, new(), agora)).Estado);
        Assert.All(await db.Agendamentos.AsNoTracking().ToListAsync(), a => Assert.Equal(1, a.Versao));
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
    }

    private static EdicaoAgendamentoApplication Application(IanDbContext db) => new(
        new PostgresAgendaEdicaoStore(db), new PostgresConsultaAgendaStore(db),
        new PostgresTipoServicoCatalogo(db), new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)));

    private static CriacaoAgendamentoApplication Criacao(IanDbContext db)
    {
        var clientes = new PostgresClienteStore(db);
        return new(new ClientesApplication(clientes), clientes, new PostgresTipoServicoCatalogo(db),
            new PostgresAgendaCriacaoStore(db), new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)));
    }

    private static CriarAgendamentoSolicitacao Criar(long updateId, TimeOnly horario) => new(
        updateId, new("Maria", "11999999999", "Endereço original"), TiposServicoPredefinidos.OrcamentoId,
        Data, horario, Duracao: 60, UnidadeDuracao: UnidadeDuracao.Minutos, EnderecoServico: "Endereço original", Observacoes: "Original");

    private static async Task<long> InitializeAsync()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
        db.TelegramUpdatesInbox.AddRange(Enumerable.Range(1, 10).Select(id => new TelegramUpdateInbox
        {
            UpdateId = id,
            PayloadJson = $$"""{"update_id":{{id}}}""",
            RecebidoEmUtc = DateTimeOffset.UtcNow
        }));
        await db.SaveChangesAsync();
        var criado = await Criacao(db).CriarAsync(Criar(1, new(8, 0)));
        Assert.Equal(EstadoCriacaoAgendamento.Criado, criado.Estado);
        return criado.AgendamentoId ?? throw new InvalidOperationException();
    }

    private static async Task<string> SnapshotAsync(IanDbContext db, long id)
    {
        var atual = await db.Agendamentos.AsNoTracking().SingleAsync(a => a.Id == id);
        var periodos = await db.PeriodosOcupacao.AsNoTracking().Where(p => p.AgendamentoId == id).OrderBy(p => p.Id).ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(new { atual, periodos });
    }

    private sealed class FailingStore(IanDbContext db) : IAgendaEdicaoStore
    {
        private readonly PostgresAgendaEdicaoStore _store = new(db);
        public Task<long?> ObterIdPorOperacaoAsync(long updateId, CancellationToken cancellationToken = default) => _store.ObterIdPorOperacaoAsync(updateId, cancellationToken);
        public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default) => _store.ObterAsync(id, cancellationToken);
        public async Task SubstituirPeriodosAsync(Agendamento agendamento, IReadOnlyList<Ian.Domain.Agenda.PeriodoCalculado> periodos,
            long updateId, CancellationToken cancellationToken = default)
        {
            await _store.SubstituirPeriodosAsync(agendamento, periodos, updateId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            throw new SyntheticFailureException();
        }
    }
    private sealed class SyntheticFailureException : Exception;
}
