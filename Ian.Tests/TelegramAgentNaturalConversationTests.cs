using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedacaoNaturalNoMesmoCicloMantemColetaReinicioEReentrega(bool sdk)
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.UseSdk = sdk;
        f.Model.Tool("esclarecer_pedido", new { }, texto: "Olá! Como posso ajudar com sua agenda?");
        await f.SendAsync(1, "Oi!");
        Assert.StartsWith("Olá!", f.Transport.Replies[^1]);
        Assert.Equal(3, f.Model.Calls);

        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 4L, data = "2030-01-07" },
            texto: "Pode me passar o telefone do cliente com DDD?");
        await f.SendAsync(2, "Quero um orçamento para Ana em 07/01/2030.");
        Assert.Equal("Pode me passar o telefone do cliente com DDD?", f.Transport.Replies[^1]);
        Assert.Single(f.Transport.Replies[^1], c => c == '?');
        Assert.DoesNotContain("nome", f.Transport.Replies[^1]);
        if (sdk) Assert.Contains(f.Model.Prompts, p => p.Contains("questionTopic"));
        else Assert.Contains(f.Model.FunctionResults, r => r.ToString().Contains("questionTopic"));
        Assert.Equal(6, f.Model.Calls); // No separate rewrite call.

        f.Restart();
        f.Model.Tool("criar_agendamento", new { telefoneCliente = "11999999999" },
            texto: "Agendei para Bruna em 08/01/2030."); // Fabricated facts must be discarded.
        await f.SendAsync(3, "O telefone é 11999999999.");
        var response = f.Transport.Replies[^1];
        Assert.Contains("Agendamento criado", response);
        Assert.Contains("Ana", response);
        Assert.Contains("07/01/2030", response);
        Assert.DoesNotContain("Bruna", response);
        Assert.DoesNotContain("telefone", response);
        var calls = f.Model.Calls;
        await f.HandleAgainAsync(3);
        Assert.Equal(calls, f.Model.Calls);
        Assert.Equal(3, f.Transport.Replies.Count);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Single(await db.Clientes.ToListAsync());
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
        Assert.DoesNotContain("11999999999", string.Join("\n", f.Logs.Messages));
    }

    [PostgresFact]
    public async Task ConsultaDeDisponibilidadePerguntaSomenteODadoAusente()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-07" });
        await f.SendAsync(1, "Verifique 07/01/2030.");
        Assert.Contains("tempo", f.Transport.Replies[^1]);
        Assert.DoesNotContain("data", f.Transport.Replies[^1]);
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-07", duracao = 1 });
        await f.SendAsync(2, "Por uma unidade.");
        Assert.Contains("minutos ou dias corridos", f.Transport.Replies[^1]);
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-07", duracao = 1, unidadeDuracao = "DiasCorridos" },
            texto: "Veja os detalhes:\nDisponível: 07/01/2030 08:00–18:00");
        await f.SendAsync(3, "Um dia corrido.");
        Assert.Contains("07/01/2030", f.Transport.Replies[^1]);
        Assert.Contains("Disponível", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task ResumoDeEscritaUsaSomenteFatosPersistidos()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(), texto:
            "Pronto, ficou agendado: Montagem para Ana, 07/01/2030 a 13/01/2030.\n7 dias corridos – 08h–18h em cada dia.\nEndereço: Rua A.");
        await f.SendAsync(1, "Marque montagem por sete dias para Ana.");
        var response = Assert.Single(f.Transport.Replies);
        Assert.Contains("Agendamento criado", response);
        Assert.Contains("Ana", response);
        Assert.Contains("07/01/2030 a 13/01/2030", response);
        Assert.Equal(3, f.Model.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Equal(7, await db.PeriodosOcupacao.CountAsync());
    }

    [PostgresTheory]
    [InlineData("timeout")]
    [InlineData("provider")]
    public async Task FalhaDaRedacaoAposEscritaMantemResultadoSemRepetirMutacao(string failure)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.FinalFailure = failure == "timeout" ? new OperationCanceledException("PRIVATE_TIMEOUT")
            : new HttpRequestException("PRIVATE_PROVIDER_ERROR");
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Orçamento para Ana.");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        var calls = f.Model.Calls;
        f.Restart();
        await f.HandleAgainAsync(1);
        Assert.Equal(calls, f.Model.Calls);
        Assert.Single(f.Transport.Replies);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
        Assert.DoesNotContain("PRIVATE_", string.Join("\n", f.Logs.Messages));
    }

    [PostgresFact]
    public async Task RedacaoNaoPodeOcultarConflitoSelecaoOuCodigoDeCancelamento()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Orçamento para Ana.");
        f.Model.Tool("criar_agendamento", Criar(servico: 4), texto: "Agendamento criado com sucesso.");
        await f.SendAsync(2, "Outro orçamento no mesmo horário.");
        Assert.Contains("conflito", f.Transport.Replies[^1]);
        Assert.DoesNotContain("criado", f.Transport.Replies[^1]);
        await f.SendAsync(3, "/descartar");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "cancelar" }, texto: "Pode responder sim.");
        await f.SendAsync(4, "Cancele o orçamento 1.");
        var code = Code(f.Transport.Replies[^1]);
        Assert.NotEmpty(code);
        f.Restart();
        var calls = f.Model.Calls;
        await f.SendAsync(5, "sim");
        Assert.Contains("código", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        await f.SendAsync(6, "confirmar " + code);
        Assert.Contains("cancelado", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(EstadoAgendamento.Cancelado, (await db.Agendamentos.SingleAsync()).Estado);
        Assert.Equal(2, await db.TelegramBusinessOperations.CountAsync());
        db.Clientes.Add(new Cliente { Nome = "Ana B", TelefoneNormalizado = "21999999999" });
        await db.SaveChangesAsync();
        f.Model.Tool("localizar_cliente", new { nome = "Ana" }, texto: "Escolhi o cliente 1.");
        await f.SendAsync(7, "Localize Ana.");
        Assert.Contains("Selecione", f.Transport.Replies[^1]);
        Assert.Contains("1:", f.Transport.Replies[^1]);
        Assert.Contains("2:", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Escolhi", f.Transport.Replies[^1]);
        f.Restart();
        calls = f.Model.Calls;
        await f.SendAsync(8, "cliente 2");
        Assert.Contains("Ana B", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
    }
}
