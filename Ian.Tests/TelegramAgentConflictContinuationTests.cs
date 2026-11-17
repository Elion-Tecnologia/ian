using System.Text.Json;
using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresTheory]
    [InlineData(false, "none")]
    [InlineData(true, "none")]
    [InlineData(false, "catalog")]
    [InlineData(true, "catalog")]
    [InlineData(false, "creation")]
    [InlineData(true, "creation")]
    public async Task ConflitoConsultaENovaDataExecutamCriacaoCompletaSemConfirmacaoExtra(bool fridayBusy, string tool)
    {
        await using var f = await Fixture.CreateAsync();
        await StartConflictedCreationAsync(f, fridayBusy);
        f.Restart();
        var plan = new AgentDialoguePlan(DialogueGoal.CriarAgendamento, DialogueReply.Coletar)
            { Texto = "Os dados estão completos. Deseja prosseguir?" };
        if (tool == "none") f.Model.Dialogue(plan);
        else
        {
            f.Model.InterpretationOverride = plan;
            // Stale tool date must not override the new date actually supplied in this turn.
            f.Model.Tool(tool == "catalog" ? "listar_servicos" : "criar_agendamento",
                tool == "catalog" ? new { } : (object)new { data = "2026-11-18" });
        }
        var calls = f.Model.Calls;
        await f.SendAsync(6, "Agende o mesmo cliente para sexta-feira");
        Assert.True(f.Model.Calls >= calls + 2); // Interpretation and real MAF execution, even without a tool call.
        Assert.DoesNotContain("deseja prosseguir", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Informe se deseja", f.Transport.Replies[^1]);
        await AssertFridayResultAsync(f, fridayBusy);
        await f.HandleAgainAsync(6);
        Assert.Equal(6, f.Transport.Replies.Count);
        if (fridayBusy)
        {
            foreach (var (id, text) in new[] { (7L, "Sim, prossiga"), (8L, "Sim") })
            {
                f.Restart();
                f.Model.Dialogue(new(DialogueGoal.Continuar, DialogueReply.Resultado));
                await f.SendAsync(id, text);
                await AssertFridayResultAsync(f, true);
                Assert.DoesNotContain("confirmar seguido", f.Transport.Replies[^1]);
            }
        }
        else
        {
            f.Restart();
            await f.SendAsync(7, "O serviço foi agendado?");
            Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
            Assert.Contains("20/11/2026", f.Transport.Replies[^1]);
        }
    }

    [PostgresTheory]
    [InlineData("Sim")]
    [InlineData("Sim, prossiga")]
    [InlineData("Pode prosseguir")]
    [InlineData("Confirmo")]
    public async Task ContinuacaoRetomaPedidoCompletoSalvoSemToolDeCriacaoNemInstrucaoDeCancelamento(string reply)
    {
        await using var f = await Fixture.CreateAsync();
        await StartConflictedCreationAsync(f, false);
        // Simulates a complete request left in collection by an earlier process/provider failure.
        f.Model.Failure = new JsonException("PRIVATE_PROVIDER_FAILURE");
        await f.SendAsync(6, "Agende o mesmo cliente para sexta-feira");
        await using (var db = PostgresTestDatabase.CreateContext())
            Assert.False(await db.Agendamentos.AnyAsync(a => a.TipoServicoId == 2));
        f.Model.Failure = null;
        f.Restart();
        f.Model.Dialogue(new(DialogueGoal.Continuar, DialogueReply.Resultado));
        await f.SendAsync(7, reply);
        await AssertFridayResultAsync(f, false);
        Assert.DoesNotContain("cancelar", f.Transport.Replies[^1]);
        await f.HandleAgainAsync(7);
        Assert.Equal(7, f.Transport.Replies.Count);
    }

    [PostgresTheory]
    [InlineData(DialogueGoal.Conversa, DialogueReply.Esclarecer, "Tudo bem")]
    [InlineData(DialogueGoal.Consulta, DialogueReply.Resultado, "Consulte a agenda")]
    [InlineData(DialogueGoal.Continuar, DialogueReply.ExplicarPedido, "Explique melhor")]
    [InlineData(DialogueGoal.Continuar, DialogueReply.Esclarecer, "Não entendi")]
    [InlineData(DialogueGoal.Continuar, DialogueReply.Resultado, "Não prossiga")]
    [InlineData(DialogueGoal.CriarAgendamento, DialogueReply.Coletar, "Talvez sexta-feira")]
    public async Task PedidoCompletoNaoExecutaEmConversaConsultaEsclarecimentoOuRecusa(DialogueGoal goal, DialogueReply act, string text)
    {
        await using var f = await Fixture.CreateAsync();
        await StartConflictedCreationAsync(f, false);
        f.Model.Failure = new JsonException("PRIVATE_PROVIDER_FAILURE");
        await f.SendAsync(6, "Agende o mesmo cliente para sexta-feira");
        f.Model.Failure = null;
        f.Restart();
        f.Model.Dialogue(new(goal, act));
        await f.SendAsync(7, text);
        Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.False(await db.Agendamentos.AnyAsync(a => a.TipoServicoId == 2));
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
        Assert.Equal(EstadoSolicitacaoPendente.Pendente, (await db.SolicitacoesPendentes.SingleAsync()).Estado);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimSemCancelamentoPendenteNaoMostraInstrucaoDeCancelarNemCriaPedidoIncompleto(bool hasPending)
    {
        await using var f = await Fixture.CreateAsync();
        if (hasPending)
        {
            f.Model.Tool("criar_agendamento", new { });
            await f.SendAsync(1, "Quero agendar");
        }
        f.Restart();
        f.Model.Dialogue(new(DialogueGoal.Continuar, DialogueReply.Resultado));
        await f.SendAsync(2, "Sim");
        Assert.DoesNotContain("cancelar", f.Transport.Replies[^1]);
        if (hasPending) Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task FalhaNaTransicaoAutomaticaReverteEscritaERetomaComandoCongeladoSemModeloOuDuplicacao()
    {
        await using var f = await Fixture.CreateAsync();
        await StartConflictedCreationAsync(f, false);
        f.Restart();
        f.Faults.FailCreation = true;
        f.Model.Dialogue(new(DialogueGoal.CriarAgendamento, DialogueReply.Resultado));
        await f.SendAsync(6, "Agende o mesmo cliente para sexta-feira");
        Assert.Equal(5, f.Transport.Replies.Count);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
            Assert.Single(await db.Clientes.ToListAsync());
            Assert.False(await db.Agendamentos.AnyAsync(a => a.TipoServicoId == 2));
            var journal = await db.Set<TelegramAgentTurn>().SingleAsync(t => t.UpdateId == 6);
            Assert.NotNull(journal.CommandJson);
            Assert.Null(journal.ResponseText);
        }
        var calls = f.Model.Calls;
        f.Restart();
        await f.MakeRetryReadyAsync(6);
        Assert.True(await f.ProcessAsync());
        await AssertFridayResultAsync(f, false);
        await f.HandleAgainAsync(6);
        Assert.Equal(calls, f.Model.Calls);
        Assert.Equal(6, f.Transport.Replies.Count);
        Assert.Contains(f.Logs.Messages, m => m.Contains("Update 6 stage=creation_transition category=ReadyToExecute", StringComparison.Ordinal));
    }

    private static async Task StartConflictedCreationAsync(Fixture f, bool fridayBusy)
    {
        f.Clock.Set(DateTimeOffset.Parse("2026-11-17T15:00:00Z"));
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var client = new Cliente { Nome = "Cliente ocupado", TelefoneNormalizado = "21999999999" };
            db.Clientes.Add(client);
            await db.SaveChangesAsync();
            // The second conflict is on Sunday, inside the seven-day occupation starting Friday.
            foreach (var day in fridayBusy ? new[] { 18, 22 } : new[] { 18 })
            {
                var start = new DateTimeOffset(2026, 11, day, 11, 0, 0, TimeSpan.Zero);
                var appointment = new Agendamento { ClienteId = client.Id, TipoServicoId = 4,
                    Modalidade = ModalidadeServico.Interno, Duracao = 1, UnidadeDuracao = UnidadeDuracao.DiasCorridos,
                    InicioPrevistoUtc = start, FimPrevistoUtc = start.AddHours(10), Estado = EstadoAgendamento.Agendado };
                db.Agendamentos.Add(appointment);
                await db.SaveChangesAsync();
                db.PeriodosOcupacao.Add(new PeriodoOcupacao { AgendamentoId = appointment.Id,
                    InicioUtc = start, FimUtc = start.AddHours(10) });
                await db.SaveChangesAsync();
            }
        }
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar uma montagem para amanhã");
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        await f.SendAsync(2, "Ana Silva");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        await f.SendAsync(3, "11999999999");
        Assert.Contains("endereço", f.Transport.Replies[^1]);
        await f.SendAsync(4, "Rua Exemplo, 66");
        Assert.Contains("conflito", f.Transport.Replies[^1]);
        string fields;
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Single(await db.Clientes.ToListAsync());
            Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
            var pending = await db.SolicitacoesPendentes.SingleAsync();
            Assert.Equal(EstadoSolicitacaoPendente.Pendente, pending.Estado);
            fields = pending.DadosColetadosJson;
        }
        f.Restart();
        f.Model.Tool("consultar_agenda", new { dataInicial = "2026-11-18" });
        await f.SendAsync(5, "Consulte os compromissos de amanhã");
        Assert.Contains("Cliente ocupado", f.Transport.Replies[^1]);
        Assert.Contains("18/11/2026", f.Transport.Replies[^1]);
        await using var queryDb = PostgresTestDatabase.CreateContext();
        Assert.Equal(fields, (await queryDb.SolicitacoesPendentes.SingleAsync()).DadosColetadosJson);
    }

    private static async Task AssertFridayResultAsync(Fixture f, bool busy)
    {
        await using var db = PostgresTestDatabase.CreateContext();
        var pending = await db.SolicitacoesPendentes.SingleAsync();
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(pending.DadosColetadosJson)!;
        var request = Assert.IsType<CriarAgendamentoToolRequest>(AgentRequestJson.Deserialize(fields["request"]));
        Assert.Equal("Ana Silva", request.NomeCliente);
        Assert.Equal("11999999999", request.TelefoneCliente);
        Assert.Equal("Rua Exemplo, 66", request.EnderecoServico);
        Assert.Equal(2, request.TipoServicoId);
        Assert.Equal(new DateOnly(2026, 11, 20), request.Data);
        if (busy)
        {
            Assert.Contains("conflito", f.Transport.Replies[^1]);
            Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
            Assert.Equal(EstadoSolicitacaoPendente.Pendente, pending.Estado);
            Assert.Single(await db.Clientes.ToListAsync());
            Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
            Assert.False(await db.Agendamentos.AnyAsync(a => a.TipoServicoId == 2));
        }
        else
        {
            Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
            Assert.Equal(EstadoSolicitacaoPendente.Confirmada, pending.Estado);
            var saved = await db.Agendamentos.SingleAsync(a => a.TipoServicoId == 2);
            Assert.Equal(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero), saved.InicioPrevistoUtc);
            Assert.Equal("Rua Exemplo, 66", saved.EnderecoSnapshot);
            var periods = await db.PeriodosOcupacao.Where(p => p.AgendamentoId == saved.Id).OrderBy(p => p.InicioUtc).ToListAsync();
            Assert.Equal(7, periods.Count);
            Assert.All(periods, p => { Assert.Equal(11, p.InicioUtc.Hour); Assert.Equal(21, p.FimUtc.Hour); });
            Assert.Equal(saved.Id, (await db.TelegramBusinessOperations.SingleAsync()).AgendamentoId);
            Assert.Equal(2, await db.Clientes.CountAsync());
        }
    }
}
