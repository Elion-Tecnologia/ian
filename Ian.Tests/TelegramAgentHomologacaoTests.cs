using System.Collections.Concurrent;
using System.Text.Json;
using Ian.Application.Agenda;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresFact]
    public async Task HomologacaoCicloCompletoDaAgendaTextualPreservaHistoricoELiberaReservas()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Orçamento de Ana em 07/01/2030.");
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-07" });
        await f.SendAsync(2, "Consulte a agenda em 07/01/2030.");
        Assert.Contains("Ana", f.Transport.Replies[^1]);
        Assert.Contains("08h–18h", f.Transport.Replies[^1]);
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "editar", observacoes = "Prioridade" });
        await f.SendAsync(3, "Anote Prioridade no agendamento 1.");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "reagendar", data = "2030-01-08" });
        await f.SendAsync(4, "Reagende o agendamento 1 para 08/01/2030.");
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-07", duracao = 1, unidadeDuracao = "DiasCorridos" });
        await f.SendAsync(5, "O dia 07 ficou disponível?");
        Assert.Contains("Disponível", f.Transport.Replies[^1]);
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "concluir" });
        await f.SendAsync(6, "Conclua o agendamento 1.");
        f.Model.Tool("criar_agendamento", Criar(servico: 4, data: "2030-01-08"));
        await f.SendAsync(7, "Outro orçamento de Ana em 08/01/2030.");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 2L, operacao = "cancelar" });
        await f.SendAsync(8, "Cancele o agendamento 2.");
        var code = Code(f.Transport.Replies[^1]);
        Assert.NotEmpty(code);
        await f.SendAsync(9, "confirmar " + code);
        Assert.Contains("cancelado", f.Transport.Replies[^1]);
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-08" });
        await f.SendAsync(10, "Consulte o histórico de 08/01/2030.");
        Assert.Contains("Concluído", f.Transport.Replies[^1]);
        Assert.Contains("Cancelado", f.Transport.Replies[^1]);
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-08", duracao = 1, unidadeDuracao = "DiasCorridos" });
        await f.SendAsync(11, "O dia 08 ficou disponível após o cancelamento?");
        Assert.Contains("Disponível", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        var historico = await db.Agendamentos.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, historico.Count);
        Assert.Equal(EstadoAgendamento.Concluido, historico[0].Estado);
        Assert.Equal(4, historico[0].Versao);
        Assert.Equal("Prioridade", historico[0].Observacoes);
        Assert.Equal(EstadoAgendamento.Cancelado, historico[1].Estado);
        Assert.Equal(2, historico[1].Versao);
        Assert.All(historico, a => Assert.Equal("Rua A", a.EnderecoSnapshot));
        Assert.Equal(2, await db.PeriodosOcupacao.CountAsync());
        Assert.Equal(4, await db.RegistrosAuditoria.CountAsync());
        Assert.Single(await db.Clientes.ToListAsync());
        Assert.All(await db.TelegramUpdatesInbox.ToListAsync(), u => Assert.Equal(EstadoTelegramUpdate.Completed, u.Estado));
        Assert.All(await db.Set<TelegramAgentTurn>().ToListAsync(), t => Assert.Equal("Accepted", t.SendState));
    }

    [PostgresTheory]
    [InlineData(1, null)]
    [InlineData(1, "08:00")]
    [InlineData(7, null)]
    [InlineData(7, "08:00")]
    public async Task HomologacaoDiasCorridosReservamJanelasCompletasInclusiveDomingo(int dias, string? horario)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 2L,
            data = "2030-01-07", duracao = dias, horario, enderecoServico = "Rua A" });
        await f.SendAsync(1, "Montagem conforme duração e horário informados.");
        await using var db = PostgresTestDatabase.CreateContext();
        var periodos = await db.PeriodosOcupacao.OrderBy(p => p.InicioUtc).ToListAsync();
        Assert.Equal(dias, periodos.Count);
        for (var i = 0; i < dias; i++)
        {
            Assert.Equal(new DateTimeOffset(2030, 1, 7, 11, 0, 0, TimeSpan.Zero).AddDays(i), periodos[i].InicioUtc);
            Assert.Equal(new DateTimeOffset(2030, 1, 7, 21, 0, 0, TimeSpan.Zero).AddDays(i), periodos[i].FimUtc);
        }
        if (dias == 7) Assert.Equal(DayOfWeek.Sunday, periodos[^1].InicioUtc.DayOfWeek);
        Assert.Contains("criado", f.Transport.Replies[^1]);
    }

    [PostgresTheory]
    [InlineData("07:59")]
    [InlineData("10:00")]
    [InlineData("18:00")]
    public async Task HomologacaoDiasCorridosNuncaCorrigeHorarioDivergenteEmSilencio(string horario)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(horario: horario), final: false);
        f.Model.Tool("criar_agendamento", Criar(horario: "08:00")); // A model retry must not accept clarification for the human.
        await f.SendAsync(1, "Montagem com início divergente.");
        Assert.Contains("08h", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.PeriodosOcupacao.ToListAsync());
        Assert.Empty(await db.Clientes.ToListAsync());
    }

    [PostgresFact]
    public async Task HomologacaoMinutosCruzaAnoExpedienteEPermiteApenasAdjacencia()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 1L,
            data = "2030-12-31", horario = "16:00", enderecoServico = "Rua A" });
        await f.SendAsync(1, "Visita técnica 31/12/2030 às 16h, quatro horas.");
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var periods = await db.PeriodosOcupacao.OrderBy(p => p.InicioUtc).ToListAsync();
            Assert.Equal(2, periods.Count);
            Assert.Equal(new DateTimeOffset(2030, 12, 31, 19, 0, 0, TimeSpan.Zero), periods[0].InicioUtc);
            Assert.Equal(new DateTimeOffset(2030, 12, 31, 21, 0, 0, TimeSpan.Zero), periods[0].FimUtc);
            Assert.Equal(new DateTimeOffset(2031, 1, 1, 11, 0, 0, TimeSpan.Zero), periods[1].InicioUtc);
            Assert.Equal(new DateTimeOffset(2031, 1, 1, 13, 0, 0, TimeSpan.Zero), periods[1].FimUtc);
        }
        f.Model.Tool("consultar_agenda", new { dataInicial = "2031-01-01" });
        await f.SendAsync(2, "Agenda no primeiro dia do ano.");
        Assert.Contains("08h–10h", f.Transport.Replies[^1]);
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 1L,
            data = "2031-01-01", horario = "09:59", duracao = 1, enderecoServico = "Rua A" });
        await f.SendAsync(3, "Visita de um minuto às 09h59.");
        Assert.Contains("conflito", f.Transport.Replies[^1]);
        f.Model.Tool("criar_agendamento", new { horario = "10:00" });
        await f.SendAsync(4, "Pode iniciar às 10h.");
        Assert.Contains("criado", f.Transport.Replies[^1]);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, await check.Agendamentos.CountAsync());
        Assert.Equal(3, await check.PeriodosOcupacao.CountAsync());
    }

    [PostgresTheory]
    [InlineData("07:59")]
    [InlineData("18:00")]
    public async Task HomologacaoMinutosForaDoExpedienteNaoDeixaCadastroParcial(string horario)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 1L,
            data = "2030-01-07", horario, enderecoServico = "Rua A" });
        await f.SendAsync(1, "Visita fora do expediente.");
        Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.Clientes.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task HomologacaoPromptUsaDataDeSaoPauloNaViradaUtc()
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(new DateTimeOffset(2030, 1, 1, 2, 30, 0, TimeSpan.Zero));
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 4L,
            data = "2030-01-01" });
        await f.SendAsync(1, "Orçamento amanhã.");
        Assert.Contains(f.Model.Prompts, p => p.Contains("Data operacional: 2029-12-31 23:30, America/Sao_Paulo"));
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 11, 0, 0, TimeSpan.Zero), (await db.Agendamentos.SingleAsync()).InicioPrevistoUtc);
    }

    [PostgresFact]
    public async Task HomologacaoContextoExpiradoNaoReutilizaClienteDoPedidoAnterior()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 4L });
        await f.SendAsync(1, "Orçamento para Ana, falta a data.");
        f.Clock.Advance(TimeSpan.FromMinutes(31));
        f.Restart();
        f.Model.Tool("criar_agendamento", new { data = "2030-01-07" });
        await f.SendAsync(2, "Dia 07/01/2030.");
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Contains(await db.SolicitacoesPendentes.ToListAsync(), p => p.Estado == EstadoSolicitacaoPendente.Expirada);
    }

    [PostgresFact]
    public async Task HomologacaoConfirmacaoExpiradaExigeNovoCodigoAposReinicio()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Crie orçamento.");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "cancelar" });
        await f.SendAsync(2, "Cancele o agendamento 1.");
        var expired = Code(f.Transport.Replies[^1]);
        f.Clock.Advance(TimeSpan.FromMinutes(5));
        f.Restart();
        await f.SendAsync(3, "confirmar " + expired);
        Assert.DoesNotContain("Agendamento cancelado", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Equal(EstadoAgendamento.Agendado, (await db.Agendamentos.SingleAsync()).Estado);
            Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
        }
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "cancelar" });
        await f.SendAsync(4, "Peça nova confirmação do cancelamento 1.");
        var current = Code(f.Transport.Replies[^1]);
        Assert.NotEmpty(current);
        Assert.NotEqual(expired, current);
        await f.SendAsync(5, "confirmar " + current);
        Assert.Contains("cancelado", f.Transport.Replies[^1]);
    }

    [PostgresFact]
    public async Task HomologacaoEdicoesConcorrentesRecusamVersaoAntigaSemEstadoParcial()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Crie orçamento.");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "editar", versaoEsperada = 1L, observacoes = "Alpha" });
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "editar", versaoEsperada = 1L, observacoes = "Beta" });
        await f.EnqueueAsync(2, "Edite com versão 1: Alpha.");
        await f.EnqueueAsync(3, "Edite com versão 1: Beta.");
        await Task.WhenAll(f.ProcessAsync(), f.ProcessAsync());
        Assert.Single(f.Transport.Replies.Skip(1), r => r.Contains("atualizado"));
        Assert.Single(f.Transport.Replies.Skip(1), r => r.Contains("conflito"));
        await using var db = PostgresTestDatabase.CreateContext();
        var atual = await db.Agendamentos.SingleAsync();
        Assert.Equal(2, atual.Versao);
        Assert.Contains(atual.Observacoes, new[] { "Alpha", "Beta" });
        Assert.Equal("Rua A", atual.EnderecoSnapshot);
        Assert.Single(await db.RegistrosAuditoria.ToListAsync());
        await f.HandleAgainAsync(2);
        await f.HandleAgainAsync(3);
        Assert.Single(await db.RegistrosAuditoria.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("editar")]
    [InlineData("concluir")]
    [InlineData("cancelar")]
    public async Task HomologacaoFalhaAposCommitRecuperaMutacaoSemModeloNemDuplicacao(string operacao)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Crie orçamento.");
        if (operacao == "cancelar")
        {
            f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao });
            await f.SendAsync(2, "Cancele o agendamento 1.");
            f.Faults.FailResponse = true;
            await f.SendAsync(3, "confirmar " + Code(f.Transport.Replies[^1]));
        }
        else
        {
            f.Faults.FailResponse = true;
            f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao, observacoes = operacao == "editar" ? "Editado" : null });
            await f.SendAsync(3, "Execute a alteração do agendamento 1.");
        }
        var calls = f.Model.Calls;
        f.Restart();
        await f.MakeRetryReadyAsync(3);
        await f.ProcessAsync();
        Assert.Equal(calls, f.Model.Calls);
        Assert.Contains("já foi salvo", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        var atual = await db.Agendamentos.SingleAsync();
        Assert.Equal(2, atual.Versao);
        Assert.Equal(operacao == "cancelar" ? EstadoAgendamento.Cancelado : operacao == "concluir" ? EstadoAgendamento.Concluido : EstadoAgendamento.Agendado, atual.Estado);
        Assert.Single(await db.RegistrosAuditoria.ToListAsync());
        Assert.Equal(2, await db.TelegramBusinessOperations.CountAsync());
        Assert.Equal("Accepted", (await db.Set<TelegramAgentTurn>().SingleAsync(t => t.UpdateId == 3)).SendState);
    }

    [PostgresTheory]
    [InlineData("editar")]
    [InlineData("concluir")]
    [InlineData("cancelar")]
    public async Task HomologacaoErroDeBancoAposGravacaoReverteMutacaoEConfirmacao(string operacao)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Crie orçamento.");
        string? code = null;
        if (operacao == "cancelar")
        {
            f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao });
            await f.SendAsync(2, "Cancele o agendamento 1.");
            code = Code(f.Transport.Replies[^1]);
        }
        await using var db = PostgresTestDatabase.CreateContext();
        var before = await HistoricoSnapshotAsync(db);
        var confirmationBefore = code is null ? null : await db.SolicitacoesPendentes.AsNoTracking()
            .SingleAsync(s => s.ConfirmacaoJson != null);
        f.Faults.FailEdit = operacao == "editar";
        f.Faults.FailClose = operacao != "editar";
        if (code is not null) await f.SendAsync(3, "confirmar " + code);
        else
        {
            f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao, observacoes = operacao == "editar" ? "Nao gravar" : null });
            await f.SendAsync(3, "Execute a alteração do agendamento 1.");
        }
        Assert.DoesNotContain("Agendamento atualizado", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Agendamento concluído", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Agendamento cancelado", f.Transport.Replies[^1]);
        Assert.Equal(before, await HistoricoSnapshotAsync(db));
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
        if (code is not null)
        {
            var confirmationAfter = await db.SolicitacoesPendentes.AsNoTracking().SingleAsync(s => s.ConfirmacaoJson != null);
            Assert.NotNull(confirmationBefore);
            Assert.Equal(confirmationBefore.Id, confirmationAfter.Id);
            Assert.Equal(EstadoSolicitacaoPendente.Pendente, confirmationAfter.Estado);
            Assert.Equal(confirmationBefore.ConfirmacaoJson, confirmationAfter.ConfirmacaoJson);
            await f.SendAsync(4, "confirmar " + code);
            Assert.Contains("cancelado", f.Transport.Replies[^1]);
        }
    }

    [PostgresFact]
    public async Task HomologacaoQuedaAposMarcarEnvioNaoReenviaRespostaIncerta()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.FailAfterBeginSend = true;
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Crie orçamento.");
        Assert.Equal(0, f.Transport.Calls);
        var calls = f.Model.Calls;
        f.Restart();
        await f.MakeRetryReadyAsync(1);
        await f.ProcessAsync();
        Assert.Equal(0, f.Transport.Calls);
        Assert.Equal(calls, f.Model.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Equal("Uncertain", (await db.Set<TelegramAgentTurn>().SingleAsync()).SendState);
        Assert.Equal(EstadoTelegramUpdate.Completed, (await db.TelegramUpdatesInbox.SingleAsync()).Estado);
    }

    [PostgresFact]
    public async Task HomologacaoLogsNaoExpoemCredenciaisTextoOuErrosPrivadosDosProvedores()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Failure = new HttpRequestException("PRIVATE_PROVIDER_ERROR");
        await f.SendAsync(1, "SEGREDO_DO_PEDIDO");
        f.Model.Failure = null;
        f.Model.Tool("criar_agendamento", Criar());
        f.Transport.Failure = "uncertain";
        await f.SendAsync(2, "Dados privados de cadastro e agendamento.");
        Assert.NotEmpty(f.Logs.Messages);
        var text = string.Join("\n", f.Logs.Messages);
        foreach (var privateValue in new[] { "synthetic-token", "synthetic-key", "synthetic-secret", "SEGREDO_DO_PEDIDO",
            "PRIVATE_PROVIDER_ERROR", "PRIVATE_TRANSPORT_ERROR", "PRIVATE_MODEL_CLAIM", "11999999999", "Rua A" })
            Assert.DoesNotContain(privateValue, text);
    }

    private static async Task<string> HistoricoSnapshotAsync(IanDbContext db) => JsonSerializer.Serialize(new
    {
        agendamentos = await db.Agendamentos.AsNoTracking().OrderBy(a => a.Id).ToListAsync(),
        periodos = await db.PeriodosOcupacao.AsNoTracking().OrderBy(p => p.Id).ToListAsync()
    });

    private sealed class AdjustableClock : TimeProvider
    {
        // PostgreSQL stores microseconds; Linux UtcNow can include a remaining 100ns tick.
        private DateTimeOffset _now = new(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset now) => _now = now;
        public void Advance(TimeSpan interval) => _now += interval;
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturedLogger(Messages);
        public void Dispose() { }
        private sealed class CapturedLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
                if (exception is not null) messages.Enqueue(exception.ToString());
            }
        }
    }

    private sealed class FaultingEdit(IanDbContext db, Faults faults) : IAgendaEdicaoStore
    {
        private readonly PostgresAgendaEdicaoStore _inner = new(db);
        public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default) => _inner.ObterAsync(id, cancellationToken);
        public Task<long?> ObterIdPorOperacaoAsync(long updateId, CancellationToken cancellationToken = default) => _inner.ObterIdPorOperacaoAsync(updateId, cancellationToken);
        public async Task SubstituirPeriodosAsync(Agendamento agendamento, IReadOnlyList<PeriodoCalculado> periodos, long updateId, CancellationToken cancellationToken = default)
        {
            await _inner.SubstituirPeriodosAsync(agendamento, periodos, updateId, cancellationToken);
            if (!faults.FailEdit) return;
            faults.FailEdit = false;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("synthetic edit failure");
        }
    }

    private sealed class FaultingClose(IanDbContext db, Faults faults) : IAgendaEncerramentoStore
    {
        private readonly PostgresAgendaEncerramentoStore _inner = new(db);
        public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default) => _inner.ObterAsync(id, cancellationToken);
        public Task<AgendamentoEncerradoItem?> ObterPorOperacaoAsync(long updateId, string operationKey, CancellationToken cancellationToken = default) =>
            _inner.ObterPorOperacaoAsync(updateId, operationKey, cancellationToken);
        public async Task RegistrarAsync(Agendamento agendamento, long updateId, string operationKey, DateTimeOffset instanteUtc, CancellationToken cancellationToken = default)
        {
            await _inner.RegistrarAsync(agendamento, updateId, operationKey, instanteUtc, cancellationToken);
            if (!faults.FailClose) return;
            faults.FailClose = false;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("synthetic close failure");
        }
    }
}
