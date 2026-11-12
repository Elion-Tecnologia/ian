using System.Text.Json;
using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresTheory]
    [InlineData("omitted")]
    [InlineData("empty")]
    [InlineData("wrong")]
    [InlineData("timeout")]
    [InlineData("tool_failure")]
    [InlineData("social")]
    [InlineData("final_incomplete")]
    public async Task SequenciaExata107PersisteMultidadosEServicoSemDependerDasTools(string failure)
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(DateTimeOffset.Parse("2026-11-12T20:00:00Z"));
        f.Model.Failure = new JsonException("PRIVATE_MODEL");
        await f.SendAsync(1, "Oi");
        f.Model.Failure = null;
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(2, "Quero agendar um serviço");
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        var calls = f.Model.Calls;
        f.Model.Failure = new TaskCanceledException("PRIVATE_TIMEOUT");
        f.Restart();
        await f.SendAsync(3, "Gabriel Ramos. Vai ser na próxima segunda-feira às 9h da manhã o início");
        var firstValid = await ReadSchedule107Async();
        Assert.Equal("Gabriel Ramos", firstValid.NomeCliente);
        Assert.Equal(new DateOnly(2026, 11, 16), firstValid.Data);
        Assert.Equal(new TimeOnly(9, 0), firstValid.Horario);
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.Contains("16/11/2026", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome do cliente", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(4, "Gabriel Ramos");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        await f.SendAsync(5, "13997209208");
        Assert.Equal("13997209208", (await ReadSchedule107Async()).TelefoneCliente);
        Assert.Contains("serviço", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(6, "Montagem");
        Assert.Equal(calls, f.Model.Calls);
        var request = await ReadSchedule107Async();
        Assert.Equal(TiposServicoPredefinidos.MontagemId, request.TipoServicoId);
        Assert.Equal(firstValid.NomeCliente, request.NomeCliente);
        Assert.Equal(firstValid.Data, request.Data);
        Assert.Equal(firstValid.Horario, request.Horario);
        Assert.Contains("endereço", f.Transport.Replies[^1]);
        Assert.Contains("08h", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Qual serviço", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Serviços:", f.Transport.Replies[^1]);
        await f.HandleAgainAsync(6);
        Assert.Equal(calls, f.Model.Calls);

        f.Model.Failure = null;
        switch (failure)
        {
            case "omitted": f.Model.Tool("criar_agendamento", new { }); break;
            case "empty": f.Model.Tool("criar_agendamento", new { nomeCliente = "", telefoneCliente = "  " }); break;
            case "wrong": f.Model.Tool("criar_agendamento", new { nomeCliente = "Gabriel", telefoneCliente = "21999999999", clienteId = 999L,
                tipoServicoId = 1L, data = "2026-11-17", horario = "10:00" }); break;
            case "timeout": f.Model.Failure = new TaskCanceledException("PRIVATE_TIMEOUT"); break;
            case "tool_failure": f.Model.Tool("criar_agendamento", new { telefoneCliente = "139" }); break;
            case "final_incomplete": f.Model.Tool("criar_agendamento", new { }, texto: ""); break;
            case "social": f.Model.InterpretationOverride = new(DialogueGoal.Conversa, DialogueReply.Esclarecer) { Texto = "Estou por aqui!" }; break;
        }
        f.Restart();
        await f.SendAsync(7, "Pode continuar");
        Assert.Equal(request, await ReadSchedule107Async());
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Empty(await db.Agendamentos.ToListAsync());
            Assert.Empty(await db.Clientes.ToListAsync());
            Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
        }
        f.Model.Failure = null;
        f.Model.InterpretationOverride = null;
        f.Model.Tool("criar_agendamento", new { enderecoServico = "Rua Fictícia", horario = "08:00" });
        f.Restart();
        await f.SendAsync(8, "O endereço é Rua Fictícia, pode começar às 08h.");
        Assert.True(f.Transport.Replies[^1].Contains("Agendamento criado", StringComparison.Ordinal),
            string.Join("\n", f.Logs.Messages.Where(m => m.Contains("Update 8 "))));
        Assert.Contains("Gabriel Ramos", f.Transport.Replies[^1]);
        Assert.Contains("16/11/2026", f.Transport.Replies[^1]);
        await f.HandleAgainAsync(8);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.Agendamentos.ToListAsync());
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
        var logs = string.Join("\n", f.Logs.Messages);
        AssertStage(f, 3, "pending_progress");
        Assert.Contains(f.Logs.Messages, m => m.Contains("Update 3 stage=pending_progress") && m.Contains("fields_present=nomeCliente,") &&
            m.Contains("missing_before=") && m.Contains("missing_after=telefone"));
        AssertStage(f, 6, "service_resolution category=Unique", "pending_progress");
        foreach (var secret in new[] { "Gabriel", "Ramos", "13997209208", "21999999999", "Rua Fict", "2026-11-16", "PRIVATE_", "synthetic-key", "synthetic-token" })
            Assert.DoesNotContain(secret, logs);
    }

    [PostgresFact]
    public async Task ConsultaESocialNaoColetamDataDeConsultaNemPerdemDadosPendentes()
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(DateTimeOffset.Parse("2026-11-13T01:30:00Z"));
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar um serviço");
        f.Model.Failure = new TaskCanceledException();
        await f.SendAsync(2, "Gabriel Ramos, telefone 13997209208, amanhã às 9h");
        var original = await ReadSchedule107Async();
        Assert.Equal(new DateOnly(2026, 11, 13), original.Data);
        f.Model.Failure = null;
        f.Model.Tool("consultar_agenda", new { dataInicial = "2026-11-20" });
        f.Restart();
        await f.SendAsync(3, "Consulte a agenda de 20/11/2026");
        Assert.Equal(original, await ReadSchedule107Async());
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Nome inventado", data = "2026-11-20", horario = "15:00" });
        f.Restart();
        await f.SendAsync(4, "Agenda de amanhã");
        Assert.Equal(original, await ReadSchedule107Async());
        var calls = f.Model.Calls;
        f.Model.InterpretationOverride = new(DialogueGoal.Conversa, DialogueReply.Esclarecer) { Texto = "Estou aqui!" };
        f.Restart();
        await f.SendAsync(5, "Oi, tudo bem?");
        Assert.Contains("Estou aqui", f.Transport.Replies[^1]);
        Assert.Equal(calls + 1, f.Model.Calls); // SocialDirect uses no Tool or rewrite.
        Assert.Equal(original, await ReadSchedule107Async());
    }

    [PostgresFact]
    public async Task ServicoAmbiguoExigeEscolhaPersistidaSemModeloEscolherPorContaPropria()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana" });
        await f.SendAsync(1, "Quero agendar um serviço para Ana");
        var calls = f.Model.Calls;
        f.Restart();
        await f.SendAsync(2, "Montagem e Ajuste");
        Assert.Contains("Selecione o serviço", f.Transport.Replies[^1]);
        Assert.Null((await ReadSchedule107Async()).TipoServicoId);
        f.Model.Tool("criar_agendamento", new { tipoServicoId = 2L });
        await f.SendAsync(3, "Escolha você");
        Assert.Null((await ReadSchedule107Async()).TipoServicoId);
        f.Restart();
        await f.SendAsync(4, "serviço 999");
        Assert.Contains("fora dos candidatos", f.Transport.Replies[^1]);
        await f.SendAsync(5, "serviço 3");
        Assert.Equal(TiposServicoPredefinidos.AjusteId, (await ReadSchedule107Async()).TipoServicoId);
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.True(f.Model.Calls >= calls);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            db.Clientes.AddRange(new Cliente { Nome = "Ana A", TelefoneNormalizado = "11999999999" },
                new Cliente { Nome = "Ana B", TelefoneNormalizado = "21999999999" });
            await db.SaveChangesAsync();
        }
        await f.SendAsync(6, "Visita técnica");
        Assert.Contains("Selecione o cliente", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(7, "cliente 2");
        Assert.Equal(TiposServicoPredefinidos.VisitaTecnicaId, (await ReadSchedule107Async()).TipoServicoId);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Empty(await check.Agendamentos.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("cadastrar_cliente")]
    [InlineData("localizar_cliente")]
    [InlineData("criar_agendamento")]
    public async Task ReformulacaoIncidentalNaoDescartaIdentidadeETelefoneJaResolvidos(string tool)
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            db.Clientes.Add(new Cliente { Nome = "Gabriel Ramos", TelefoneNormalizado = "13997209208" });
            await db.SaveChangesAsync();
        }
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Gabriel Ramos" });
        await f.SendAsync(1, "Quero agendar um serviço para Gabriel Ramos");
        var original = await ReadSchedule107Async();
        Assert.NotNull(original.ClienteId);
        Assert.Equal("13997209208", original.TelefoneCliente);
        f.Restart();
        if (tool == "criar_agendamento")
            f.Model.Tool(tool, new { nomeCliente = "Gabriel", clienteId = 999L });
        else f.Model.Tool(tool, new { nome = "Gabriel" });
        await f.SendAsync(2, "Pode continuar");
        Assert.Equal(original, await ReadSchedule107Async());
        Assert.DoesNotContain("telefone", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome do cliente", f.Transport.Replies[^1]);
    }

    private static async Task<CriarAgendamentoToolRequest> ReadSchedule107Async()
    {
        await using var db = PostgresTestDatabase.CreateContext();
        var pending = await db.SolicitacoesPendentes.SingleAsync(p => p.Estado == EstadoSolicitacaoPendente.Pendente);
        return (CriarAgendamentoToolRequest)AgentRequestJson.Deserialize(
            JsonSerializer.Deserialize<Dictionary<string, string>>(pending.DadosColetadosJson)!["request"]);
    }
}
