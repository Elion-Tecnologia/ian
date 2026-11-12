using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PedidoNaturalIniciaColetaEPreservaMontagemAteTelefoneHorarioERetomada(bool sdk)
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.UseSdk = sdk;
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar um serviço.");
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        Assert.DoesNotContain("operações", f.Transport.Replies[^1]);
        Assert.Equal("NONE", f.Model.ToolModes[0]);

        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L,
            data = "2030-01-07", horario = "10:00", enderecoServico = "Rua Fictícia, 123" });
        await f.SendAsync(2, "Montagem para Ana, Rua Fictícia, 123, dia 07/01/2030 às 10h.");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.Contains("10:00", f.Transport.Replies[^1]);
        Assert.Contains("08h–18h", f.Transport.Replies[^1]);
        Assert.DoesNotContain("informe nome", f.Transport.Replies[^1]);

        f.Restart();
        f.Model.Tool("cadastrar_cliente", new { nome = "Ana", telefone = "11999999999" });
        await f.SendAsync(3, "O telefone é 11999999999.");
        Assert.DoesNotContain("telefone", f.Transport.Replies[^1]); // Already collected; only the requested time remains unresolved.
        Assert.Contains("10:00", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Empty(await db.Clientes.ToListAsync());
            Assert.Empty(await db.Agendamentos.ToListAsync());
            var pending = await db.SolicitacoesPendentes.SingleAsync();
            Assert.Contains("CriarAgendamento", pending.Operacao);
            Assert.Contains("10:00", pending.DadosColetadosJson);
        }

        f.Restart();
        f.Transport.BeforeReply = async _ =>
        {
            await using var committed = PostgresTestDatabase.CreateContext();
            Assert.Single(await committed.Agendamentos.ToListAsync());
            Assert.Equal(7, await committed.PeriodosOcupacao.CountAsync());
        };
        f.Model.Tool("criar_agendamento", new { horario = "08:00" });
        await f.SendAsync(4, "Pode iniciar às 08h.");
        await using (var db = PostgresTestDatabase.CreateContext()) Assert.Single(await db.Agendamentos.ToListAsync());
        var resumo = f.Transport.Replies[^1];
        foreach (var value in new[] { "Agendamento criado", "Montagem", "Ana", "07/01/2030 a 13/01/2030", "7 dias corridos", "08h–18h", "Rua Fictícia, 123" })
            Assert.Contains(value, resumo);
        foreach (var tecnico in new[] { "Operação concluída", "Cliente 1", "Agendamento 1", "DiasCorridos", "Success", "PRIVATE_MODEL_CLAIM" })
            Assert.DoesNotContain(tecnico, resumo);
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-09" });
        await f.SendAsync(5, "Como ficou a agenda de 09/01/2030?");
        Assert.Contains("Montagem", f.Transport.Replies[^1]);
        Assert.Contains("Ana", f.Transport.Replies[^1]);
        await f.EnqueueAsync(4, "Pode iniciar às 08h.");
        Assert.False(await f.ProcessAsync());
        await f.HandleAgainAsync(4);
        Assert.Equal(5, f.Transport.Replies.Count);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.Clientes.ToListAsync());
        Assert.Single(await check.Agendamentos.ToListAsync());
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("cadastrar_cliente", false)]
    [InlineData("localizar_cliente", false)]
    [InlineData("cadastrar_cliente", true)]
    [InlineData("localizar_cliente", true)]
    public async Task DadosDaToolDeClienteContinuamObjetivoOriginalSemEscritaIntermediaria(string tool, bool existente)
    {
        await using var f = await Fixture.CreateAsync();
        if (existente)
        {
            await using var db = PostgresTestDatabase.CreateContext();
            db.Clientes.Add(new Cliente { Nome = "Ana", TelefoneNormalizado = "11999999999" });
            await db.SaveChangesAsync();
        }
        f.Model.Tool("criar_agendamento", new { tipoServicoId = 2L, data = "2030-01-07", enderecoServico = "Rua A" });
        await f.SendAsync(1, "Quero agendar montagem em 07/01/2030 na Rua A.");
        f.Restart();
        f.Model.Tool(tool, new { nome = "Ana", telefone = "11999999999" }, final: false);
        f.Model.Tool("listar_servicos", new { }); // The intermediate lookup cannot replace the final saved result.
        await f.SendAsync(2, "É para Ana, telefone 11999999999.");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains("Montagem para Ana", f.Transport.Replies[^1]);
        Assert.DoesNotContain("cadastrado/localizado", f.Transport.Replies[^1]);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.Clientes.ToListAsync());
        var saved = await check.Agendamentos.SingleAsync();
        Assert.Equal("Rua A", saved.EnderecoSnapshot);
        Assert.Equal(7, saved.Duracao);
        Assert.Equal("agendamento:create", (await check.TelegramBusinessOperations.SingleAsync()).OperationKey);
    }

    [PostgresFact]
    public async Task AmbiguidadeNaEtapaDeClienteMantemPedidoDeMontagemAposSelecaoExplicita()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            db.Clientes.AddRange(new Cliente { Nome = "Ana A", TelefoneNormalizado = "11999999999" },
                new Cliente { Nome = "Ana B", TelefoneNormalizado = "21999999999" });
            await db.SaveChangesAsync();
        }
        f.Model.Tool("criar_agendamento", new { tipoServicoId = 2L, data = "2030-01-07", enderecoServico = "Rua A" });
        await f.SendAsync(1, "Quero montagem em 07/01/2030 na Rua A.");
        f.Model.Tool("localizar_cliente", new { nome = "Ana" });
        await f.SendAsync(2, "É para Ana.");
        Assert.Contains("Selecione", f.Transport.Replies[^1]);
        f.Model.Tool("cadastrar_cliente", new { nome = "Ana A", telefone = "11999999999" });
        await f.SendAsync(3, "Escolha você.");
        await using (var db = PostgresTestDatabase.CreateContext()) Assert.Empty(await db.Agendamentos.ToListAsync());
        f.Restart();
        await f.SendAsync(4, "cliente 2");
        Assert.Contains("Montagem para Ana B", f.Transport.Replies[^1]);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, (await check.Agendamentos.SingleAsync()).ClienteId);
    }

    [PostgresFact]
    public async Task TelefoneIsoladoCompletaPedidoMesmoComModeloIndisponivel()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L, data = "2030-01-07", enderecoServico = "Rua A" });
        await f.SendAsync(1, "Montagem para Ana em 07/01/2030 na Rua A.");
        f.Model.Failure = new HttpRequestException("PRIVATE_PROVIDER_ERROR");
        var calls = f.Model.Calls;
        await f.SendAsync(2, "11999999999");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains("Montagem para Ana", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        f.Restart();
        await f.HandleAgainAsync(2);
        Assert.Equal(calls, f.Model.Calls);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.Agendamentos.ToListAsync());
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FalhaNaContinuidadePreservaAtomicidadeOuRecuperaCommitSemDuplicar(bool aposCommit)
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L, data = "2030-01-07", enderecoServico = "Rua A" });
        await f.SendAsync(1, "Montagem para Ana em 07/01/2030 na Rua A.");
        f.Faults.FailCreation = !aposCommit;
        f.Faults.FailResponse = aposCommit;
        f.Model.Tool("cadastrar_cliente", new { nome = "Ana", telefone = "11999999999" });
        await f.SendAsync(2, "Telefone 11999999999.");
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Equal(aposCommit ? 1 : 0, await db.Clientes.CountAsync());
            Assert.Equal(aposCommit ? 1 : 0, await db.Agendamentos.CountAsync());
            Assert.DoesNotContain(f.Transport.Replies, r => r.Contains("Agendamento criado"));
        }
        var calls = f.Model.Calls;
        f.Restart();
        if (aposCommit)
        {
            await f.MakeRetryReadyAsync(2);
            Assert.True(await f.ProcessAsync());
            Assert.Equal(calls, f.Model.Calls);
            Assert.Contains("já foi salvo", f.Transport.Replies[^1]);
        }
        else
        {
            f.Model.Tool("localizar_cliente", new { telefone = "11999999999" });
            await f.SendAsync(3, "Tente novamente com o mesmo telefone.");
            Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        }
        await f.HandleAgainAsync(2);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.Clientes.ToListAsync());
        Assert.Single(await check.Agendamentos.ToListAsync());
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task RespostasIguaisDeUpdatesDistintosSaoLegitimasMasRedeliveryNaoReenvia()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar um serviço.");
        var calls = f.Model.Calls;
        await f.EnqueueAsync(1, "Quero agendar um serviço.");
        Assert.False(await f.ProcessAsync());
        await f.HandleAgainAsync(1);
        Assert.Equal(calls, f.Model.Calls);
        Assert.Single(f.Transport.Replies);
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(2, "Quero agendar um serviço.");
        Assert.Equal(2, f.Transport.Replies.Count);
        Assert.Equal(f.Transport.Replies[0], f.Transport.Replies[1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, await db.TelegramUpdatesInbox.CountAsync());
        Assert.Equal(2, await db.Set<TelegramAgentTurn>().CountAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task IntencaoDesconhecidaPodePedirEsclarecimentoSemCriarOperacao()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("esclarecer_pedido", new { });
        await f.SendAsync(1, "Olá!");
        Assert.Equal("Como posso ajudar com sua agenda?", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.SolicitacoesPendentes.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task EsclarecimentoNaoDescartaObjetivoNemExecutaPedidoPendente()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L, data = "2030-01-07", enderecoServico = "Rua A" });
        await f.SendAsync(1, "Montagem para Ana em 07/01/2030 na Rua A.");
        f.Model.Tool("esclarecer_pedido", new { });
        await f.SendAsync(2, "Não entendi.");
        Assert.Contains("agendamento continua pendente", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext()) Assert.Empty(await db.Agendamentos.ToListAsync());
        f.Restart();
        f.Model.Tool("cadastrar_cliente", new { telefone = "11999999999" });
        await f.SendAsync(3, "O telefone é 11999999999.");
        Assert.Contains("Montagem para Ana", f.Transport.Replies[^1]);
    }

    [PostgresFact]
    public async Task MetadadosDeCatalogoEClienteFicamNasToolsERespostasSaoNaturais()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("listar_servicos", new { });
        await f.SendAsync(1, "O que posso agendar?");
        Assert.Contains("Montagem: 7 dias corridos", f.Transport.Replies[^1]);
        Assert.DoesNotContain("ID", f.Transport.Replies[^1]);
        Assert.DoesNotContain("DiasCorridos", f.Transport.Replies[^1]);
        var catalogo = f.Model.FunctionResults.Last(r => r.GetProperty("message").GetString()?.StartsWith("Serviços:") == true);
        Assert.Contains(catalogo.GetProperty("data").EnumerateArray(), t => t.GetProperty("id").GetInt64() == 2);
        var montagem = catalogo.GetProperty("data").EnumerateArray().Single(t => t.GetProperty("id").GetInt64() == 2);
        Assert.Equal("DiasCorridos", montagem.GetProperty("unidadeDuracaoPadrao").GetString());
        Assert.Equal("Externo", montagem.GetProperty("modalidadePadrao").GetString());
        f.Model.Tool("cadastrar_cliente", new { nome = "Ana", telefone = "11999999999" });
        await f.SendAsync(2, "Cadastre Ana, 11999999999.");
        Assert.Equal("Cadastrei Ana com o telefone 11999999999.", f.Transport.Replies[^1]);
        f.Model.Tool("localizar_cliente", new { telefone = "11999999999" });
        await f.SendAsync(3, "Localize pelo mesmo telefone.");
        Assert.Equal("Ana, telefone 11999999999", f.Transport.Replies[^1]);
        var cliente = f.Model.FunctionResults.Last().GetProperty("data").EnumerateArray().Single();
        Assert.Equal("Ana", cliente.GetProperty("nome").GetString());
        Assert.True(cliente.GetProperty("id").GetInt64() > 0);
    }
}
