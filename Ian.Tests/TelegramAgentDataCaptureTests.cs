using System.Text.Json;
using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SequenciaReal105CapturaNomeTelefoneNaPrimeiraTentativaEPreservaSegundaAsNove(bool existing)
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(DateTimeOffset.Parse("2026-11-13T01:30:00Z")); // Thursday evening in São Paulo.
        if (existing)
        {
            await using var db = PostgresTestDatabase.CreateContext();
            db.Clientes.Add(new Cliente { Nome = "Cliente existente", TelefoneNormalizado = "13997209208" });
            await db.SaveChangesAsync();
        }
        f.Model.Failure = new JsonException("PRIVATE_INVALID_MODEL_RESPONSE");
        await f.SendAsync(1, "Oi IAN, tudo bem?");
        Assert.Contains("Estou por aqui", f.Transport.Replies[^1]);
        Assert.DoesNotContain("reformular", f.Transport.Replies[^1]);
        Assert.Empty(f.Model.FunctionResults);
        f.Model.Failure = null;
        f.Model.Tool("listar_servicos", new { }); // Wrong intermediate choice must not expose the catalogue.
        await f.SendAsync(2, "Eu queria agendar um serviço para segunda-feira de manhã às 9h");
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Serviços:", f.Transport.Replies[^1]);
        var calls = f.Model.Calls;
        f.Model.Failure = new JsonException("PRIVATE_MODEL_MUST_NOT_BE_NEEDED");
        f.Restart();
        await f.SendAsync(3, "Gabriel Ramos");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        await f.HandleAgainAsync(3);
        f.Restart();
        await f.SendAsync(4, "13997209208");
        Assert.Contains("serviço", f.Transport.Replies[^1]);
        Assert.DoesNotContain("telefone", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Serviços:", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        f.Model.Failure = null;
        f.Model.Tool("criar_agendamento", new { nomeCliente = "", telefoneCliente = "  " });
        await f.SendAsync(5, "Pode continuar.");
        Assert.Contains("serviço", f.Transport.Replies[^1]);
        Assert.DoesNotContain("telefone", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var pending = await db.SolicitacoesPendentes.SingleAsync();
            var request = (CriarAgendamentoToolRequest)AgentRequestJson.Deserialize(JsonSerializer.Deserialize<Dictionary<string, string>>(pending.DadosColetadosJson)!["request"]);
            Assert.Equal(existing ? "Cliente existente" : "Gabriel Ramos", request.NomeCliente);
            Assert.Equal("13997209208", request.TelefoneCliente);
            Assert.Equal(new DateOnly(2026, 11, 16), request.Data);
            Assert.Equal(new TimeOnly(9, 0), request.Horario);
            Assert.Null(request.TipoServicoId);
            Assert.Empty(await db.Agendamentos.ToListAsync());
            Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
            Assert.Equal(existing ? 1 : 0, await db.Clientes.CountAsync());
        }
        f.Model.Tool("listar_servicos", new { });
        await f.SendAsync(6, "Quais serviços posso escolher?");
        Assert.Contains("Serviços:", f.Transport.Replies[^1]);
        // Service names now resolve directly from the backend catalog.
        await f.SendAsync(7, "Visita técnica.");
        Assert.Contains("endereço", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome", f.Transport.Replies[^1]);
        Assert.DoesNotContain("data", f.Transport.Replies[^1]);
        f.Restart();
        f.Model.Tool("criar_agendamento", new { enderecoServico = "Rua Fictícia" });
        await f.SendAsync(8, "É na Rua Fictícia.");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains("16/11/2026", f.Transport.Replies[^1]);
        Assert.Contains("09:00", f.Transport.Replies[^1]);
        calls = f.Model.Calls;
        await f.HandleAgainAsync(8);
        Assert.Equal(calls, f.Model.Calls);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.Agendamentos.ToListAsync());
        Assert.Single(await check.Clientes.ToListAsync());
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
        Assert.Equal(8, f.Transport.Replies.Count);
        var logs = string.Join("\n", f.Logs.Messages);
        Assert.Contains("stage=pending_data_captured", logs);
        Assert.Contains("category=SocialInvalidResponse", logs);
        foreach (var value in new[] { "Gabriel Ramos", "13997209208", "Rua Fictícia", "PRIVATE_" }) Assert.DoesNotContain(value, logs);
    }

    [PostgresFact]
    public async Task SaudacaoConsultaETelefoneInvalidoNaoViraramNomeNemAvancaramColetaErrada()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar.");
        f.Model.Failure = new JsonException("PRIVATE_MODEL_FAILURE");
        f.Restart();
        await f.SendAsync(2, "Oi IAN, tudo bem?");
        Assert.Contains("Estou por aqui", f.Transport.Replies[^1]);
        await f.SendAsync(3, "Quero consultar agenda");
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        await f.SendAsync(4, "Gabriel Ramos");
        var calls = f.Model.Calls;
        await f.SendAsync(5, "00997209208");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        var pending = await db.SolicitacoesPendentes.SingleAsync();
        Assert.Contains("Gabriel Ramos", pending.DadosColetadosJson);
        Assert.DoesNotContain("00997209208", pending.DadosColetadosJson);
        Assert.Contains("telefone", pending.CamposFaltantesJson);
        Assert.Contains("CriarAgendamento", pending.Operacao);
        Assert.Empty(await db.Agendamentos.ToListAsync());
    }
}
