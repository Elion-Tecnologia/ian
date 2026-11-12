using System.Globalization;
using System.Text.Json;
using Ian.Application.Agenda;
using Ian.Application.AI;
using Ian.Application.Clientes;
using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Ian.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class EncerramentoAgendamentoPostgresTests
{
    private static readonly DateOnly Data = new(2030, 1, 7);
    private static readonly AgentToolCaller Ator = new(123, 123, true, true, true);

    [PostgresFact]
    public async Task ConclusaoAntecipadaLiberaReservasFuturasEPreservaHistoricoConsultavel()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clock = new TestClock(); // During the second of three reserved days.
        var antes = await DadosHistoricosAsync(db, id);
        Assert.Equal(EstadoDisponibilidadeAgenda.Indisponivel, (await Disponibilidade(db).VerificarAsync(new(Data.AddDays(2), new(8, 0), 60, UnidadeDuracao.Minutos))).Estado);

        var resultado = await Application(db, clock).ConcluirAsync(new(2, id, 1));
        Assert.Equal(AgentToolOutcome.Success, resultado.Outcome);
        Assert.Equal(new AgendamentoEncerradoItem(id, EstadoAgendamento.Concluido, 2), resultado.Data);
        Assert.Equal(antes, await DadosHistoricosAsync(db, id));
        Assert.Equal(3, await db.PeriodosOcupacao.CountAsync());
        Assert.Equal(EstadoDisponibilidadeAgenda.Disponivel, (await Disponibilidade(db).VerificarAsync(new(Data.AddDays(2), new(8, 0), 60, UnidadeDuracao.Minutos))).Estado);
        await AssertHistoricoAsync(db, id, EstadoAgendamento.Concluido);
        var auditoria = await db.RegistrosAuditoria.SingleAsync();
        Assert.Equal("Concluido", auditoria.Resultado);
        Assert.Equal(clock.GetUtcNow(), auditoria.RegistradoEmUtc);
        Assert.Equal(id, (await db.TelegramBusinessOperations.SingleAsync(o => o.UpdateId == 2)).AgendamentoId);
        var novo = await Criacao(db).CriarAsync(Criar(3, Data.AddDays(2)));
        Assert.Equal(EstadoCriacaoAgendamento.Criado, novo.Estado);
    }

    [PostgresFact]
    public async Task ConclusaoReentregueRetornaAlvoOriginalENovoUpdateNaoRepeteTransicao()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var app = Application(db, new TestClock());
        Assert.Equal(AgentToolOutcome.Success, (await app.ConcluirAsync(new(2, id, 1))).Outcome);
        var reentrega = await app.ConcluirAsync(new(2, long.MaxValue, 1));
        Assert.Equal(new AgendamentoEncerradoItem(id, EstadoAgendamento.Concluido, 2), reentrega.Data);
        Assert.Equal(AgentToolOutcome.Conflict, (await app.ConcluirAsync(new(3, id, 2))).Outcome);
        Assert.Equal(2, (await db.Agendamentos.AsNoTracking().SingleAsync()).Versao);
        Assert.Single(await db.RegistrosAuditoria.ToListAsync());
        Assert.False(await db.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 3));
    }

    [PostgresFact]
    public async Task CancelamentoConfirmadoAposReinicioLiberaAgendaESoConsomeUmaVez()
    {
        var id = await InitializeAsync();
        var clock = new TestClock();
        ConfirmationAcceptance acceptance;
        string antes;
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            antes = await DadosHistoricosAsync(db, id);
            acceptance = await PrepararAsync(db, clock, id);
        }
        await using var restarted = PostgresTestDatabase.CreateContext();
        var app = Application(restarted, clock);
        var resultado = await app.CancelarAsync(new(2, Ator, acceptance));
        Assert.Equal(AgentToolOutcome.Success, resultado.Outcome);
        Assert.Equal(new AgendamentoEncerradoItem(id, EstadoAgendamento.Cancelado, 2), resultado.Data);
        foreach (var update in new long[] { 2, 3 })
            Assert.Equal(AgentToolOutcome.Conflict, (await app.CancelarAsync(new(update, Ator, acceptance))).Outcome);
        Assert.Equal(antes, await DadosHistoricosAsync(restarted, id));
        await AssertHistoricoAsync(restarted, id, EstadoAgendamento.Cancelado);
        Assert.Equal(EstadoSolicitacaoPendente.Confirmada, (await restarted.SolicitacoesPendentes.AsNoTracking().SingleAsync()).Estado);
        Assert.Single(await restarted.RegistrosAuditoria.ToListAsync());
        Assert.Equal(id, (await restarted.TelegramBusinessOperations.SingleAsync(o => o.UpdateId == 2)).AgendamentoId);
        Assert.False(await restarted.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 3));
        Assert.Equal(EstadoCriacaoAgendamento.Criado, (await Criacao(restarted).CriarAsync(Criar(4, Data))).Estado);
    }

    [PostgresFact]
    public async Task SemConfirmacaoExpiradaOuComAtorAlvoArgumentosInvalidosNaoCancela()
    {
        foreach (var cenario in new[] { "ausente", "nao-preparada", "expirada", "ator", "grupo", "sem-texto", "alvo", "versao", "desafio", "argumentos", "intencao" })
        {
            var id = await InitializeAsync();
            await using var db = PostgresTestDatabase.CreateContext();
            var clock = new TestClock();
            var app = Application(db, clock);
            var antes = await SnapshotAsync(db);
            var acceptance = await PrepararAsync(db, clock, id);
            var caller = Ator;
            switch (cenario)
            {
                case "expirada": clock.Advance(TimeSpan.FromMinutes(5)); break;
                case "ator": caller = Ator with { UserId = 999 }; break;
                case "grupo": caller = Ator with { IsPrivateChat = false, ChatId = -1 }; break;
                case "sem-texto": caller = Ator with { HasText = false }; break;
                case "alvo": acceptance = acceptance with { Arguments = acceptance.Arguments with { AgendamentoId = long.MaxValue } }; break;
                case "versao": acceptance = acceptance with { Version = 2 }; break;
                case "desafio": acceptance = acceptance with { ChallengeId = Guid.NewGuid() }; break;
                case "argumentos": acceptance = acceptance with { Arguments = acceptance.Arguments with { Operacao = "concluir" } }; break;
                case "nao-preparada":
                    await db.SolicitacoesPendentes.ExecuteUpdateAsync(s => s.SetProperty(p => p.ConfirmacaoJson, (string?)null));
                    break;
                case "intencao": await Conversa(db).CancelAsync(Ator.UserId, clock.GetUtcNow()); break;
            }
            var resultado = await app.CancelarAsync(new(2, caller, cenario == "ausente" ? null : acceptance));
            Assert.NotEqual(AgentToolOutcome.Success, resultado.Outcome);
            Assert.Equal(antes, await SnapshotAsync(db));
            Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
            Assert.False(await db.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 2));
        }
    }

    [PostgresFact]
    public async Task EdicaoInvalidaConfirmacaoAntigaENovaConfirmacaoUsaVersaoAtual()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clock = new TestClock();
        var acceptance = await PrepararAsync(db, clock, id);
        Assert.Equal(EstadoEdicaoAgendamento.Editado, (await Edicao(db).EditarAsync(new(3, id, 1, Observacoes: "Atualizada"))).Estado);
        var antes = await SnapshotAsync(db);
        var app = Application(db, clock);
        Assert.Equal(AgentToolOutcome.Conflict, (await app.CancelarAsync(new(2, Ator, acceptance))).Outcome);
        Assert.Equal(antes, await SnapshotAsync(db));
        var challengeResult = await app.PrepararCancelamentoAsync(Ator, acceptance.PendingRequestId, id);
        var challenge = Assert.IsType<ConfirmationChallenge>(challengeResult.Data);
        Assert.Equal(2, challenge.Version);
        var atualizada = acceptance with { ChallengeId = challenge.ChallengeId, Version = challenge.Version };
        Assert.Equal(AgentToolOutcome.Success, (await app.CancelarAsync(new(2, Ator, atualizada))).Outcome);
        Assert.Equal(3, (await db.Agendamentos.AsNoTracking().SingleAsync()).Versao);
    }

    [PostgresFact]
    public async Task ConclusaoRecusaAusenciaVersaoObsoletaEEstadosTerminaisSemReabrir()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clock = new TestClock();
        var app = Application(db, clock);
        var antes = await SnapshotAsync(db);
        Assert.Equal(AgentToolOutcome.Conflict, (await app.ConcluirAsync(new(2, long.MaxValue, 1))).Outcome);
        Assert.Equal(AgentToolOutcome.Conflict, (await app.ConcluirAsync(new(2, id, 2))).Outcome);
        Assert.Equal(antes, await SnapshotAsync(db));
        foreach (var estado in new[] { EstadoAgendamento.Concluido, EstadoAgendamento.Cancelado })
        {
            await db.Agendamentos.ExecuteUpdateAsync(s => s.SetProperty(a => a.Estado, estado));
            antes = await SnapshotAsync(db);
            Assert.Equal(AgentToolOutcome.Conflict, (await app.ConcluirAsync(new(2, id, 1))).Outcome);
            Assert.Equal(EstadoEdicaoAgendamento.EstadoNaoPermitido, (await Edicao(db).EditarAsync(new(3, id, 1))).Estado);
            var pending = await Conversa(db).StartAsync(Ator.UserId, PendingIntent.AlterarAgendamento,
                Campos(id), [], true, clock.GetUtcNow());
            Assert.Equal(AgentToolOutcome.Conflict, (await app.PrepararCancelamentoAsync(Ator, Assert.IsType<PendingConversationState>(pending.State).Id, id)).Outcome);
            Assert.Equal(antes, await SnapshotAsync(db));
        }
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task CancelamentosConcorrentesSoConfirmamUmaTransicao()
    {
        var id = await InitializeAsync();
        var clock = new TestClock();
        await using var db = PostgresTestDatabase.CreateContext();
        var acceptance = await PrepararAsync(db, clock, id);
        await using var outro = PostgresTestDatabase.CreateContext();
        var resultados = await Task.WhenAll(
            Application(db, clock).CancelarAsync(new(2, Ator, acceptance)),
            Application(outro, clock).CancelarAsync(new(3, Ator, acceptance)));
        Assert.Single(resultados, r => r.Outcome == AgentToolOutcome.Success);
        Assert.Single(resultados, r => r.Outcome == AgentToolOutcome.Conflict);
        await AssertUmaTransicaoAsync(db);
    }

    [PostgresFact]
    public async Task ConclusaoECancelamentoConcorrentesNaoSobrescrevemEstadoTerminal()
    {
        var id = await InitializeAsync();
        var clock = new TestClock();
        await using var db = PostgresTestDatabase.CreateContext();
        var acceptance = await PrepararAsync(db, clock, id);
        await using var outro = PostgresTestDatabase.CreateContext();
        await outro.Agendamentos.SingleAsync(); // Stale tracking must be cleared before the locked read.
        var resultados = await Task.WhenAll(
            Application(db, clock).CancelarAsync(new(2, Ator, acceptance)),
            Application(outro, clock).ConcluirAsync(new(3, id, 1)));
        Assert.Single(resultados, r => r.Outcome == AgentToolOutcome.Success);
        Assert.Single(resultados, r => r.Outcome == AgentToolOutcome.Conflict);
        await AssertUmaTransicaoAsync(db);
        var atual = await db.Agendamentos.AsNoTracking().SingleAsync();
        var pending = await db.SolicitacoesPendentes.AsNoTracking().SingleAsync();
        Assert.Equal(atual.Estado == EstadoAgendamento.Cancelado ? EstadoSolicitacaoPendente.Confirmada : EstadoSolicitacaoPendente.Pendente, pending.Estado);
    }

    [PostgresFact]
    public async Task DuasConclusoesConcorrentesDaMesmaVersaoSoGravamUmaVez()
    {
        var id = await InitializeAsync();
        var clock = new TestClock();
        await using var db = PostgresTestDatabase.CreateContext();
        await using var outro = PostgresTestDatabase.CreateContext();
        var resultados = await Task.WhenAll(
            Application(db, clock).ConcluirAsync(new(2, id, 1)),
            Application(outro, clock).ConcluirAsync(new(3, id, 1)));
        Assert.Single(resultados, r => r.Outcome == AgentToolOutcome.Success);
        Assert.Single(resultados, r => r.Outcome == AgentToolOutcome.Conflict);
        await AssertUmaTransicaoAsync(db);
    }

    [PostgresFact]
    public async Task EdicaoECancelamentoConcorrentesRevalidamVersaoSobMesmoLock()
    {
        var id = await InitializeAsync();
        var clock = new TestClock();
        await using var db = PostgresTestDatabase.CreateContext();
        var acceptance = await PrepararAsync(db, clock, id);
        await using var outro = PostgresTestDatabase.CreateContext();
        var cancelamento = Application(db, clock).CancelarAsync(new(2, Ator, acceptance));
        var edicao = Edicao(outro).EditarAsync(new(3, id, 1, Observacoes: "Mudou"));
        await Task.WhenAll(cancelamento, edicao);
        var cancelado = (await cancelamento).Outcome == AgentToolOutcome.Success;
        Assert.Equal(!cancelado, (await edicao).Estado == EstadoEdicaoAgendamento.Editado);
        var atual = await db.Agendamentos.AsNoTracking().SingleAsync();
        Assert.Equal(2, atual.Versao);
        Assert.Equal(cancelado ? EstadoAgendamento.Cancelado : EstadoAgendamento.Agendado, atual.Estado);
        Assert.Equal(cancelado ? "Original" : "Mudou", atual.Observacoes);
        Assert.Single(await db.RegistrosAuditoria.ToListAsync());
    }

    [PostgresFact]
    public async Task FalhaAposGravacaoReverteEstadoVersaoAuditoriaVinculoEConsumoDaConfirmacao()
    {
        foreach (var cancelar in new[] { false, true })
        {
            var id = await InitializeAsync();
            await using var db = PostgresTestDatabase.CreateContext();
            var clock = new TestClock();
            var acceptance = cancelar ? await PrepararAsync(db, clock, id) : null;
            var antes = await SnapshotAsync(db);
            var app = Application(db, clock, new FalhaAposGravarStore(db, () => throw new SyntheticFailureException()));
            Task<AgentToolResult> Executar() => cancelar
                ? app.CancelarAsync(new(2, Ator, acceptance)) : app.ConcluirAsync(new(2, id, 1));
            await Assert.ThrowsAsync<SyntheticFailureException>(Executar);
            Assert.Equal(antes, await SnapshotAsync(db));
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
            Assert.False(await db.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 2));
            if (cancelar)
                Assert.Equal(EstadoSolicitacaoPendente.Pendente, (await db.SolicitacoesPendentes.AsNoTracking().SingleAsync()).Estado);
            Assert.Equal(EstadoDisponibilidadeAgenda.Indisponivel, (await Disponibilidade(db).VerificarAsync(new(Data.AddDays(2), new(8, 0), 60, UnidadeDuracao.Minutos))).Estado);
            var retry = Application(db, clock);
            Assert.Equal(AgentToolOutcome.Success, (await (cancelar
                ? retry.CancelarAsync(new(2, Ator, acceptance)) : retry.ConcluirAsync(new(2, id, 1)))).Outcome);
        }
    }

    [PostgresFact]
    public async Task ConfirmacaoQueExpiraDuranteCancelamentoReverteTodaAOperacao()
    {
        var id = await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var clock = new TestClock();
        var acceptance = await PrepararAsync(db, clock, id);
        var antes = await SnapshotAsync(db);
        var app = Application(db, clock, new FalhaAposGravarStore(db, () => clock.Advance(TimeSpan.FromMinutes(5))));
        Assert.Equal(AgentToolOutcome.Conflict, (await app.CancelarAsync(new(2, Ator, acceptance))).Outcome);
        Assert.Equal(antes, await SnapshotAsync(db));
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
        Assert.False(await db.TelegramBusinessOperations.AnyAsync(o => o.UpdateId == 2));
        Assert.Equal(EstadoSolicitacaoPendente.Pendente, (await db.SolicitacoesPendentes.AsNoTracking().SingleAsync()).Estado);
    }

    private static async Task AssertUmaTransicaoAsync(IanDbContext db)
    {
        Assert.Equal(2, (await db.Agendamentos.AsNoTracking().SingleAsync()).Versao);
        Assert.Equal(3, await db.PeriodosOcupacao.CountAsync());
        Assert.Single(await db.RegistrosAuditoria.ToListAsync());
        Assert.Equal(2, await db.TelegramBusinessOperations.CountAsync());
    }

    private static async Task AssertHistoricoAsync(IanDbContext db, long id, EstadoAgendamento estado)
    {
        var consulta = new ConsultaAgendaApplication(new PostgresConsultaAgendaStore(db));
        var porId = await consulta.ConsultarAsync(new(AgendamentoId: id));
        Assert.Equal(estado, Assert.Single(porId.Agendamentos).Estado);
        var porData = await consulta.ConsultarAsync(new(Data.AddDays(2), Estado: estado));
        Assert.Equal(id, Assert.Single(porData.Agendamentos).Id);
    }

    private static EncerramentoAgendamentoApplication Application(IanDbContext db, TestClock clock, IAgendaEncerramentoStore? store = null) =>
        new(store ?? new PostgresAgendaEncerramentoStore(db), new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)),
            new PostgresOperationConfirmation(db, new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)), Telegram(), clock), clock);

    private static IOptions<TelegramOptions> Telegram() => Options.Create(new TelegramOptions { AdministratorUserId = Ator.UserId });
    private static PendingConversationService Conversa(IanDbContext db) => new(new PostgresPendingConversationRepository(db, Telegram()));
    private static DisponibilidadeAgendaApplication Disponibilidade(IanDbContext db) => new(new PostgresConsultaAgendaStore(db));
    private static EdicaoAgendamentoApplication Edicao(IanDbContext db) => new(new PostgresAgendaEdicaoStore(db), new PostgresConsultaAgendaStore(db),
        new PostgresTipoServicoCatalogo(db), new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)));
    private static CriacaoAgendamentoApplication Criacao(IanDbContext db)
    {
        var store = new PostgresClienteStore(db);
        return new(new ClientesApplication(store), store, new PostgresTipoServicoCatalogo(db),
            new PostgresAgendaCriacaoStore(db), new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)));
    }
    private static CriarAgendamentoSolicitacao Criar(long update, DateOnly data) => new(update,
        new("Maria", "11999999999", "Endereço original"), TiposServicoPredefinidos.MontagemId,
        data, null, Duracao: 3, EnderecoServico: "Endereço original", Observacoes: "Original");

    private static Dictionary<string, string> Campos(long id) => new()
    {
        ["agendamentoId"] = id.ToString(CultureInfo.InvariantCulture), ["operacao"] = "cancelar"
    };
    private static async Task<ConfirmationAcceptance> PrepararAsync(IanDbContext db, TestClock clock, long id)
    {
        var pending = await Conversa(db).StartAsync(Ator.UserId, PendingIntent.AlterarAgendamento, Campos(id), [], false, clock.GetUtcNow());
        var requestId = Assert.IsType<PendingConversationState>(pending.State).Id;
        var result = await Application(db, clock).PrepararCancelamentoAsync(Ator, requestId, id);
        Assert.Equal(AgentToolOutcome.ConfirmationRequired, result.Outcome);
        var challenge = Assert.IsType<ConfirmationChallenge>(result.Data);
        return new(requestId, challenge.ChallengeId, challenge.Version, new(id, "cancelar", null, null, null));
    }
    private static async Task<long> InitializeAsync()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
        db.TelegramUpdatesInbox.AddRange(Enumerable.Range(1, 10).Select(id => new TelegramUpdateInbox
        {
            UpdateId = id, PayloadJson = "{}", RecebidoEmUtc = new TestClock().GetUtcNow()
        }));
        await db.SaveChangesAsync();
        var result = await Criacao(db).CriarAsync(Criar(1, Data));
        Assert.Equal(EstadoCriacaoAgendamento.Criado, result.Estado);
        return Assert.IsType<long>(result.AgendamentoId);
    }
    private static async Task<string> DadosHistoricosAsync(IanDbContext db, long id)
    {
        var atual = await db.Agendamentos.AsNoTracking().SingleAsync(a => a.Id == id);
        var periodos = await db.PeriodosOcupacao.AsNoTracking().Where(p => p.AgendamentoId == id).OrderBy(p => p.Id).ToListAsync();
        return JsonSerializer.Serialize(new { atual.Id, atual.ClienteId, atual.TipoServicoId, atual.Modalidade, atual.Duracao,
            atual.UnidadeDuracao, atual.InicioPrevistoUtc, atual.FimPrevistoUtc, atual.EnderecoSnapshot, atual.Observacoes, periodos });
    }
    private static async Task<string> SnapshotAsync(IanDbContext db) => JsonSerializer.Serialize(new
    {
        agendamentos = await db.Agendamentos.AsNoTracking().OrderBy(a => a.Id).ToListAsync(),
        periodos = await db.PeriodosOcupacao.AsNoTracking().OrderBy(p => p.Id).ToListAsync()
    });
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2030, 1, 8, 14, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
    private sealed class SyntheticFailureException : Exception;
    private sealed class FalhaAposGravarStore(IanDbContext db, Action afterSave) : IAgendaEncerramentoStore
    {
        private readonly PostgresAgendaEncerramentoStore _store = new(db);
        public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default) => _store.ObterAsync(id, cancellationToken);
        public Task<AgendamentoEncerradoItem?> ObterPorOperacaoAsync(long updateId, string key, CancellationToken cancellationToken = default) =>
            _store.ObterPorOperacaoAsync(updateId, key, cancellationToken);
        public async Task RegistrarAsync(Agendamento agendamento, long updateId, string operationKey, DateTimeOffset instanteUtc,
            CancellationToken cancellationToken = default)
        {
            await _store.RegistrarAsync(agendamento, updateId, operationKey, instanteUtc, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            afterSave();
        }
    }
}
