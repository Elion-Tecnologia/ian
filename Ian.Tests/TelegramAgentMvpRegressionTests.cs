using System.Text.Json;
using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresFact]
    public async Task EnderecoCompletaSequenciaRealAposReinicioSemModeloNemDuplicarOperacaoOuResposta()
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(DateTimeOffset.Parse("2026-11-16T20:00:00Z"));
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar um serviço");
        f.Model.Failure = new JsonException("PRIVATE_MODEL_UNAVAILABLE");
        foreach (var (id, text, next) in new[] {
            (2L, "Gabriel Ramos", "telefone"), (3L, "13997209208", "serviço"),
            (4L, "Montagem", "data"), (5L, "18/11/2026", "endereço") })
        {
            f.Restart();
            await f.SendAsync(id, text);
            Assert.Contains(next, f.Transport.Replies[^1]);
            await using var incomplete = PostgresTestDatabase.CreateContext();
            Assert.Empty(await incomplete.Agendamentos.ToListAsync());
            Assert.Empty(await incomplete.Clientes.ToListAsync());
        }
        f.Restart();
        var calls = f.Model.Calls;
        await f.SendAsync(6, "Rua Exemplo, 66");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains("Rua Exemplo, 66", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        await f.EnqueueAsync(6, "Rua Exemplo, 66");
        Assert.False(await f.ProcessAsync());
        await f.HandleAgainAsync(6);
        f.Restart();
        await f.SendAsync(7, "O serviço foi agendado?");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        var saved = Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Equal("Rua Exemplo, 66", saved.EnderecoSnapshot);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.Zero), saved.InicioPrevistoUtc);
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
        Assert.Single(await db.Clientes.ToListAsync());
        var journal = await db.Set<TelegramAgentTurn>().SingleAsync(t => t.UpdateId == 6);
        var attempt = await db.Set<TelegramReplyAttempt>().SingleAsync(t => t.UpdateId == 6);
        Assert.Equal("Accepted", journal.SendState);
        Assert.Equal(journal.TelegramMessageId, attempt.TelegramMessageId);
        Assert.NotNull(attempt.TelegramMessageId);
        Assert.Equal(1, journal.SendAttempts);
        Assert.Equal(7, f.Transport.Replies.Count);
    }

    [PostgresFact]
    public async Task PerguntaDeEstadoNaoExecutaPedidoPendenteNemDependeDoModelo()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar");
        f.Model.Failure = new JsonException("PRIVATE_FAILURE");
        f.Restart();
        await f.SendAsync(2, "O serviço foi agendado?");
        Assert.Contains("pendente", f.Transport.Replies[^1]);
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsultaIntercaladaPrevaleceSobreToolDeClienteSemApagarCriacaoPendente(bool finalFailure)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar");
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.Consulta, DialogueReply.Resultado);
        f.Model.Tool("localizar_cliente", new { }, final: false);
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-09" });
        if (finalFailure) f.Model.FinalFailure = new JsonException("PRIVATE_FINAL_FAILURE");
        await f.SendAsync(2, "Consulte a agenda em 09/01/2030");
        Assert.Contains("Nenhum agendamento", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome do cliente", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        var pending = Assert.Single(await db.SolicitacoesPendentes.ToListAsync());
        Assert.Equal(EstadoSolicitacaoPendente.Pendente, pending.Estado);
        Assert.DoesNotContain("2030-01-09", pending.DadosColetadosJson);
        Assert.Empty(await db.Agendamentos.ToListAsync());
    }

    [PostgresFact]
    public async Task ConsultaPorClienteSemPeriodoRetornaRegistrosMesmoDistantesMaisDe31Dias()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 4, data = "2030-01-07" });
        await f.SendAsync(1, "Agende orçamento para Ana");
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 4, data = "2030-03-07" });
        await f.SendAsync(2, "Agende outro orçamento para Ana");
        await using var db = PostgresTestDatabase.CreateContext();
        var client = await db.Clientes.SingleAsync();
        f.Restart();
        f.Model.Tool("consultar_agenda", new { clienteId = client.Id });
        await f.SendAsync(3, "Consulte os agendamentos da Ana");
        Assert.Contains("07/01/2030", f.Transport.Replies[^1]);
        Assert.Contains("07/03/2030", f.Transport.Replies[^1]);
        Assert.Contains("[#1]", f.Transport.Replies[^1]);
        Assert.Contains("[#2]", f.Transport.Replies[^1]);
    }

    [PostgresTheory]
    [InlineData(null, 2, 1)]
    [InlineData(null, 4, 2)]
    [InlineData("Cancelado", 2, 0)]
    [InlineData("Concluido", 4, 1)]
    [InlineData("Cancelado", 4, 1)]
    public async Task ConsultaViaToolsCombinaFiltrosConsideraOcupacaoProlongadaEFuso(string? state, long service, int count)
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(DateTimeOffset.Parse("2026-11-19T02:30:00Z")); // Still November 18 in São Paulo.
        await using var db = PostgresTestDatabase.CreateContext();
        var client = new Cliente { Nome = "Ana", TelefoneNormalizado = "11999999999" };
        db.Clientes.Add(client);
        await db.SaveChangesAsync();
        var ids = new List<long>();
        foreach (var status in new[] { EstadoAgendamento.Agendado, EstadoAgendamento.Concluido, EstadoAgendamento.Cancelado })
        {
            var longService = status == EstadoAgendamento.Agendado;
            var start = new DateTimeOffset(2026, 11, longService ? 16 : 18, 11, 0, 0, TimeSpan.Zero);
            var appointment = new Agendamento { ClienteId = client.Id, TipoServicoId = longService ? 2 : 4,
                Modalidade = ModalidadeServico.Interno, Duracao = longService ? 7 : 1,
                UnidadeDuracao = UnidadeDuracao.DiasCorridos, InicioPrevistoUtc = start,
                FimPrevistoUtc = start.AddDays(longService ? 6 : 0).AddHours(10), Estado = status };
            db.Agendamentos.Add(appointment);
            await db.SaveChangesAsync();
            ids.Add(appointment.Id);
            db.PeriodosOcupacao.AddRange(Enumerable.Range(0, longService ? 7 : 1).Select(day =>
                new PeriodoOcupacao { AgendamentoId = appointment.Id, InicioUtc = start.AddDays(day), FimUtc = start.AddDays(day).AddHours(10) }));
            await db.SaveChangesAsync();
        }
        f.Restart();
        f.Model.Tool("consultar_agenda", new { dataInicial = "2026-11-18", dataFinal = "2026-11-18",
            clienteId = client.Id, tipoServicoId = service, estado = state });
        await f.SendAsync(1, "Consulte os serviços de hoje com os filtros solicitados");
        var response = f.Transport.Replies[^1];
        Assert.Contains("2026-11-18 23:30", f.Model.Prompts[0]);
        if (count == 0) Assert.Contains("Nenhum agendamento", response);
        else
        {
            Assert.Contains("18/11/2026", response);
            Assert.Contains("08h–18h", response);
        }
        var expectedIds = await db.Agendamentos.Where(a => a.TipoServicoId == service).ToListAsync();
        foreach (var id in ids)
        {
            var expected = expectedIds.Any(a => a.Id == id && (state is null || a.Estado.ToString() == state));
            Assert.Equal(expected, response.Contains($"[#{id}]", StringComparison.Ordinal));
        }
        Assert.Equal(count, response.Split("[#").Length - 1);
        Assert.Equal(3, await db.Agendamentos.CountAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task FalhaTransacionalNoUltimoCampoNuncaConfirmaCriacaoNemNaPerguntaDeEstado()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.FailCreation = true;
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999",
            tipoServicoId = 2, data = "2030-01-07" });
        await f.SendAsync(1, "Agende montagem para Ana");
        f.Model.Failure = new JsonException("PRIVATE_FAILURE");
        f.Restart();
        await f.SendAsync(2, "Rua Exemplo, 66");
        Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains("Não consegui concluir", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(3, "O serviço foi agendado?");
        Assert.Contains("não tem criação confirmada", f.Transport.Replies[^1]);
        Assert.Contains("pendente", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.Clientes.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("invalid_state")]
    [InlineData("reversed_dates")]
    [InlineData("invalid_service")]
    public async Task ConsultaComFiltrosInvalidosNaoInventaResultadoNemGravaDados(string failure)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-09",
            dataFinal = failure == "reversed_dates" ? "2030-01-08" : "2030-01-09",
            tipoServicoId = failure == "invalid_service" ? 0 : 2,
            estado = failure == "invalid_state" ? "Reagendado" : "Agendado" });
        await f.SendAsync(1, "Consulte os serviços usando estes filtros");
        Assert.DoesNotContain("Nenhum agendamento", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains(failure == "invalid_state" ? "estado válido" : "conferir", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("")]
    [InlineData("Rua Inferida, 99")]
    public async Task ToolNaoApagaNemSubstituiEnderecoConfirmadoEnquantoColetaOutroCampo(string inferredAddress)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999",
            tipoServicoId = 1, data = "2030-01-07", enderecoServico = "Rua Confirmada, 66" });
        await f.SendAsync(1, "Agende visita técnica para Ana na Rua Confirmada, 66");
        Assert.Contains("horário", f.Transport.Replies[^1]);
        f.Restart();
        f.Model.Tool("criar_agendamento", new { enderecoServico = inferredAddress });
        await f.SendAsync(2, "Pode continuar");
        Assert.Contains("horário", f.Transport.Replies[^1]);
        await using (var incomplete = PostgresTestDatabase.CreateContext())
            Assert.Empty(await incomplete.Agendamentos.ToListAsync());
        f.Restart();
        f.Model.Failure = new JsonException("PRIVATE_MODEL_UNAVAILABLE");
        await f.SendAsync(3, "Pode começar às 09h");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal("Rua Confirmada, 66", (await db.Agendamentos.SingleAsync()).EnderecoSnapshot);
    }
}
