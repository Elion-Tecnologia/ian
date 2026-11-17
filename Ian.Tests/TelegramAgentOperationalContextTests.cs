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
    public async Task ContextoTipadoConduzDuracaoAbsolutaRelativaEnderecoReagendamentoEConclusaoAposReinicios(bool sdk)
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.UseSdk = sdk;
        f.Restart();
        await CreateJoaoAsync(f);
        f.Model.Tool("localizar_cliente", new { nome = "João" }, final: false);
        f.Model.Tool("consultar_agenda", new { usarContexto = true, tipoServicoId = 2, estado = "Agendado" });
        await f.SendAsync(2, "Localize o João e consulte as montagens dele");
        Assert.Contains("Montagem", f.Transport.Replies[^1]);
        Assert.Contains("João", f.Transport.Replies[^1]);
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado);
        f.Model.Tool("alterar_agendamento", new { operacao = "editar", usarContexto = true, duracao = 3, unidadeDuracao = "DiasCorridos" });
        await f.SendAsync(3, "Diminua a duração da montagem para três dias");
        await AssertEditedAsync(f, 3, 2, "Rua Inicial, 10", 7);
        f.Restart();
        f.Model.Tool("alterar_agendamento", new { operacao = "editar", usarContexto = true, deltaDuracao = -2, unidadeDuracao = "DiasCorridos" });
        await f.SendAsync(4, "Reduza esse serviço em dois dias");
        await AssertEditedAsync(f, 1, 3, "Rua Inicial, 10", 7);
        var calls = f.Model.Calls;
        await f.HandleAgainAsync(4);
        Assert.Equal(calls, f.Model.Calls);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var frozen = Assert.IsType<AlterarAgendamentoToolRequest>(AgentRequestJson.Deserialize(
                (await db.Set<TelegramAgentTurn>().SingleAsync(t => t.UpdateId == 4)).CommandJson!));
            Assert.Null(frozen.DeltaDuracao);
            Assert.Equal(1, frozen.Duracao);
            Assert.Equal(2, frozen.VersaoEsperada);
        }
        f.Model.Tool("alterar_agendamento", new { operacao = "editar", usarContexto = true, campoPendente = "Endereco" });
        await f.SendAsync(5, "Altere o endereço dele");
        Assert.Contains("novo endereço", f.Transport.Replies[^1]);
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.Consulta, DialogueReply.Resultado);
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-20", duracao = 1, unidadeDuracao = "DiasCorridos" }, final: false);
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-20" });
        await f.SendAsync(6, "Consulte os compromissos e a disponibilidade do dia 20");
        Assert.Contains("Nenhum agendamento", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var pending = await db.SolicitacoesPendentes.SingleAsync(p => p.Estado == EstadoSolicitacaoPendente.Pendente);
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(pending.DadosColetadosJson)!;
            var request = Assert.IsType<AlterarAgendamentoToolRequest>(AgentRequestJson.Deserialize(fields["request"]));
            Assert.Equal(1, request.AgendamentoId);
            Assert.Equal(3, request.VersaoEsperada);
            Assert.Equal("Endereco", request.CampoPendente);
        }
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado);
        // Incidental model IDs/versions cannot retarget an already bound pending edit.
        f.Model.Tool("alterar_agendamento", new { enderecoServico = "Rua Nova, 88", agendamentoId = 999, versaoEsperada = 999 });
        await f.SendAsync(7, "Rua Nova, 88");
        await AssertEditedAsync(f, 1, 4, "Rua Nova, 88", 7);
        f.Restart();
        f.Model.Tool("alterar_agendamento", new { operacao = "reagendar", usarContexto = true, data = "2030-01-11" });
        await f.SendAsync(8, "Passe esse agendamento para sexta-feira");
        await AssertEditedAsync(f, 1, 5, "Rua Nova, 88", 11);
        f.Restart();
        f.Model.Tool("alterar_agendamento", new { operacao = "concluir", usarContexto = true });
        await f.SendAsync(9, "Esse serviço já foi concluído");
        Assert.Contains("Agendamento concluído", f.Transport.Replies[^1]);
        await using var finalDb = PostgresTestDatabase.CreateContext();
        var saved = await finalDb.Agendamentos.SingleAsync();
        Assert.Equal(EstadoAgendamento.Concluido, saved.Estado);
        Assert.Equal(6, saved.Versao);
        Assert.Equal("Armário", saved.Observacoes);
        Assert.Equal(ModalidadeServico.Externo, saved.Modalidade);
        Assert.Single(await finalDb.PeriodosOcupacao.ToListAsync());
        Assert.Equal(6, await finalDb.TelegramBusinessOperations.CountAsync());
        var context = JsonSerializer.Deserialize<AgentOperationalContext>((await finalDb.Set<TelegramAgentTurn>()
            .SingleAsync(t => t.UpdateId == 9)).OperationalContextJson!, AgentRequestJson.Options)!;
        Assert.Equal(EstadoAgendamento.Concluido, context.SelectedAppointment?.Estado);
        Assert.DoesNotContain("Rua", JsonSerializer.Serialize(context));
        Assert.DoesNotContain("11999999999", JsonSerializer.Serialize(context));
    }

    [PostgresFact]
    public async Task ConsultaAmbiguaNaoAutorizaEscolhaDoModeloECancelamentoSobreviveConsultaIntercalada()
    {
        await using var f = await Fixture.CreateAsync();
        await CreateJoaoAsync(f);
        f.Model.Tool("criar_agendamento", new { nomeCliente = "João", telefoneCliente = "11999999999", tipoServicoId = 2,
            data = "2030-01-20", enderecoServico = "Rua Inicial, 10" });
        await f.SendAsync(2, "Agende outra montagem para João no dia 20");
        f.Model.Tool("consultar_agenda", new { usarContexto = true });
        await f.SendAsync(3, "Consulte os agendamentos dele");
        Assert.Contains("[#1]", f.Transport.Replies[^1]);
        Assert.Contains("[#2]", f.Transport.Replies[^1]);
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado);
        f.Model.Tool("alterar_agendamento", new { operacao = "cancelar", usarContexto = true, agendamentoId = 1 });
        await f.SendAsync(4, "Desmarque a montagem do João");
        Assert.Contains("Selecione", f.Transport.Replies[^1]);
        Assert.DoesNotContain("confirmar ", f.Transport.Replies[^1]);
        f.Model.Tool("alterar_agendamento", new { operacao = "cancelar", agendamentoId = 1 });
        await f.SendAsync(5, "Esse primeiro");
        Assert.DoesNotContain("confirmar ", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(6, "agendamento 1");
        var code = Code(f.Transport.Replies[^1]);
        Assert.NotEmpty(code);
        f.Model.InterpretationOverride = new(DialogueGoal.Consulta, DialogueReply.Resultado);
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-20" });
        await f.SendAsync(7, "Consulte também o dia 20");
        Assert.DoesNotContain(code, f.Model.Prompts[^1]);
        var calls = f.Model.Calls;
        await f.SendAsync(8, "Sim");
        Assert.Contains("código", f.Transport.Replies[^1]);
        Assert.Equal(calls, f.Model.Calls);
        f.Restart();
        await f.SendAsync(9, "confirmar " + code);
        Assert.Contains("Agendamento cancelado", f.Transport.Replies[^1]);
        await f.HandleAgainAsync(9);
        await f.SendAsync(10, "confirmar " + code);
        Assert.DoesNotContain("Agendamento cancelado", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, await db.Agendamentos.CountAsync());
        Assert.Equal(EstadoAgendamento.Cancelado, (await db.Agendamentos.SingleAsync(a => a.Id == 1)).Estado);
        Assert.Equal(EstadoAgendamento.Agendado, (await db.Agendamentos.SingleAsync(a => a.Id == 2)).Estado);
        Assert.Equal(3, await db.TelegramBusinessOperations.CountAsync());
    }

    [PostgresTheory]
    [InlineData("relative_conflict")]
    [InlineData("reschedule_conflict")]
    [InlineData("stale")]
    [InlineData("negative")]
    [InlineData("mixed")]
    [InlineData("unit")]
    public async Task AlteracaoContextualInvalidaNaoMudaDadosPeriodosOuEstado(string failure)
    {
        await using var f = await Fixture.CreateAsync();
        await CreateJoaoAsync(f);
        if (failure.EndsWith("conflict", StringComparison.Ordinal))
        {
            f.Model.Tool("criar_agendamento", new { nomeCliente = "Maria", telefoneCliente = "21999999999", tipoServicoId = 4, data = "2030-01-18" });
            await f.SendAsync(2, "Agende orçamento da Maria no dia 18");
        }
        f.Model.Tool("consultar_agenda", new { agendamentoId = 1 });
        await f.SendAsync(3, "Consulte o agendamento 1");
        await using (var db = PostgresTestDatabase.CreateContext())
            if (failure == "stale") await db.Agendamentos.Where(a => a.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(a => a.Versao, 2));
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado);
        f.Model.Tool("alterar_agendamento", new { usarContexto = true, operacao = failure == "reschedule_conflict" ? "reagendar" : "editar",
            data = failure == "reschedule_conflict" ? "2030-01-12" : null,
            deltaDuracao = failure == "reschedule_conflict" ? (int?)null : failure == "relative_conflict" ? 5 : failure == "negative" ? -7 : -2,
            duracao = failure == "mixed" ? (int?)3 : null, unidadeDuracao = failure == "unit" ? "Minutos" : "DiasCorridos" });
        await f.SendAsync(4, "Altere esse serviço conforme solicitado");
        Assert.DoesNotContain("Agendamento atualizado", f.Transport.Replies[^1]);
        if (failure.EndsWith("conflict", StringComparison.Ordinal) || failure == "stale") Assert.Contains("conflito", f.Transport.Replies[^1]);
        await using var finalDb = PostgresTestDatabase.CreateContext();
        var original = await finalDb.Agendamentos.SingleAsync(a => a.Id == 1);
        Assert.Equal(7, original.Duracao);
        Assert.Equal(failure == "stale" ? 2 : 1, original.Versao);
        Assert.Equal("Rua Inicial, 10", original.EnderecoSnapshot);
        Assert.Equal(7, original.InicioPrevistoUtc.Day);
        Assert.Equal(7, await finalDb.PeriodosOcupacao.CountAsync(p => p.AgendamentoId == 1));
        Assert.Equal(failure.EndsWith("conflict", StringComparison.Ordinal) ? 2 : 1, await finalDb.TelegramBusinessOperations.CountAsync());
    }

    [PostgresFact]
    public async Task ReferenciaExpiradaNaoEscolheUnicoAgendamentoGlobalEHistoricoEnviadoEhMinimizado()
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(DateTimeOffset.Parse("2030-01-01T12:00:00Z"));
        await CreateJoaoAsync(f);
        f.Restart();
        f.Model.Dialogue(new(DialogueGoal.Conversa, DialogueReply.Esclarecer) { Texto = "Como posso ajudar?" });
        await f.SendAsync(2, "Oi");
        Assert.DoesNotContain("11999999999", f.Model.Prompts[^1]);
        Assert.DoesNotContain("Rua Inicial", f.Model.Prompts[^1]);
        Assert.True(f.Model.Prompts[^1].Length < AgentExecutionLimits.MaxPromptCharacters);
        f.Clock.Set(f.Clock.GetUtcNow().AddMinutes(31));
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado);
        f.Model.Tool("alterar_agendamento", new { usarContexto = true, operacao = "concluir" });
        await f.SendAsync(3, "Esse serviço já foi concluído");
        Assert.Contains("Consulte ou selecione", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(EstadoAgendamento.Agendado, (await db.Agendamentos.SingleAsync()).Estado);
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task ClienteLocalizadoEhReutilizadoNaCriacaoSemModeloReenviarNomeTelefoneOuEndereco()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            db.Clientes.Add(new Cliente { Nome = "João", TelefoneNormalizado = "11999999999", EnderecoPrincipal = "Rua Cadastro, 55" });
            await db.SaveChangesAsync();
        }
        f.Model.Tool("localizar_cliente", new { nome = "João" });
        await f.SendAsync(1, "Localize o João");
        f.Restart();
        f.Model.Tool("criar_agendamento", new { usarContexto = true, tipoServicoId = 2, data = "2030-01-07" });
        await f.SendAsync(2, "Agende uma montagem para esse cliente no dia 7");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        await using var saved = PostgresTestDatabase.CreateContext();
        Assert.Single(await saved.Clientes.ToListAsync());
        var appointment = await saved.Agendamentos.SingleAsync();
        Assert.Equal(1, appointment.ClienteId);
        Assert.Equal("Rua Cadastro, 55", appointment.EnderecoSnapshot);
    }

    [PostgresFact]
    public async Task IntencaoTipadaSobreviveClienteAmbiguoERetomaConsultaEToolSemRepetirPedido()
    {
        await using var f = await Fixture.CreateAsync();
        await CreateJoaoAsync(f);
        f.Model.Tool("criar_agendamento", new { nomeCliente = "João B", telefoneCliente = "21999999999", tipoServicoId = 2,
            data = "2030-01-20", enderecoServico = "Rua B, 20" });
        await f.SendAsync(2, "Agende a montagem de João B no dia 20");
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado) { Operacao = AppointmentOperation.Cancelar };
        f.Model.Tool("localizar_cliente", new { nome = "João" });
        await f.SendAsync(3, "Desmarque a montagem do João");
        Assert.Contains("Selecione um cliente", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var pending = await db.SolicitacoesPendentes.SingleAsync(p => p.Estado == EstadoSolicitacaoPendente.Pendente);
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(pending.DadosColetadosJson)!;
            Assert.Equal("cancelar", Assert.IsType<AlterarAgendamentoToolRequest>(AgentRequestJson.Deserialize(fields["request"])).Operacao);
            Assert.Equal(2, await db.TelegramBusinessOperations.CountAsync());
        }
        f.Restart();
        f.Model.Tool("consultar_agenda", new { usarContexto = true, tipoServicoId = 2, estado = "Agendado" }, final: false);
        f.Model.Tool("alterar_agendamento", new { usarContexto = true }); // Operation comes from the durable request.
        await f.SendAsync(4, "cliente 2");
        var code = Code(f.Transport.Replies[^1]);
        Assert.NotEmpty(code);
        await f.SendAsync(5, "confirmar " + code);
        Assert.Contains("Agendamento cancelado", f.Transport.Replies[^1]);
        await using var saved = PostgresTestDatabase.CreateContext();
        Assert.Equal(EstadoAgendamento.Agendado, (await saved.Agendamentos.SingleAsync(a => a.Id == 1)).Estado);
        Assert.Equal(EstadoAgendamento.Cancelado, (await saved.Agendamentos.SingleAsync(a => a.Id == 2)).Estado);
        Assert.Equal(3, await saved.TelegramBusinessOperations.CountAsync());
    }

    [PostgresFact]
    public async Task IdDoModeloNaoPodeSubstituirReferenciaUnicaMasTextoComIdExplicitoPodeSelecionarOutroAlvo()
    {
        await using var f = await Fixture.CreateAsync();
        await CreateJoaoAsync(f);
        f.Model.Tool("criar_agendamento", new { nomeCliente = "João", telefoneCliente = "11999999999", tipoServicoId = 2,
            data = "2030-01-20", enderecoServico = "Rua Inicial, 10" });
        await f.SendAsync(2, "Agende outra montagem do João no dia 20");
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado) { Operacao = AppointmentOperation.Concluir };
        f.Model.Tool("alterar_agendamento", new { usarContexto = true, operacao = "concluir", agendamentoId = 1 });
        await f.SendAsync(3, "Esse serviço já foi concluído");
        Assert.Contains("não corresponde", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.All(await db.Agendamentos.ToListAsync(), a => Assert.Equal(EstadoAgendamento.Agendado, a.Estado));
            Assert.Equal(2, await db.TelegramBusinessOperations.CountAsync());
        }
        f.Model.Tool("alterar_agendamento", new { operacao = "concluir", agendamentoId = 1 });
        await f.SendAsync(4, "Conclua o agendamento 1");
        Assert.Contains("Agendamento concluído", f.Transport.Replies[^1]);
        await using var saved = PostgresTestDatabase.CreateContext();
        Assert.Equal(EstadoAgendamento.Concluido, (await saved.Agendamentos.SingleAsync(a => a.Id == 1)).Estado);
        Assert.Equal(EstadoAgendamento.Agendado, (await saved.Agendamentos.SingleAsync(a => a.Id == 2)).Estado);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComandoRelativoCongeladoRecuperaFalhaSemReaplicarDeltaEAdmiteCamposOpcionaisAusentes(bool rollback)
    {
        await using var f = await Fixture.CreateAsync();
        await CreateJoaoAsync(f);
        f.Faults.FailEdit = rollback;
        f.Faults.FailResponse = true;
        f.Model.InterpretationOverride = new(DialogueGoal.AlterarAgendamento, DialogueReply.Resultado);
        f.Model.Tool("alterar_agendamento", new { usarContexto = true, operacao = "editar", deltaDuracao = -2, unidadeDuracao = "DiasCorridos" });
        await f.SendAsync(2, "Reduza esse serviço em dois dias");
        Assert.Single(f.Transport.Replies);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Equal(rollback ? 7 : 5, (await db.Agendamentos.SingleAsync()).Duracao);
            var journal = await db.Set<TelegramAgentTurn>().SingleAsync(t => t.UpdateId == 2);
            var frozen = System.Text.Json.Nodes.JsonNode.Parse(journal.CommandJson!)!.AsObject();
            Assert.Equal(5, frozen["duracao"]!.GetValue<int>());
            Assert.Null(frozen["deltaDuracao"]);
            // Older journals omit fields added to optional Tool contracts.
            frozen.Remove("deltaDuracao");
            frozen.Remove("usarContexto");
            frozen.Remove("campoPendente");
            journal.CommandJson = frozen.ToJsonString();
            await db.SaveChangesAsync();
        }
        var calls = f.Model.Calls;
        f.Restart();
        await f.HandleAgainAsync(2);
        Assert.Equal(calls, f.Model.Calls);
        Assert.Equal(2, f.Transport.Replies.Count);
        await AssertEditedAsync(f, 5, 2, "Rua Inicial, 10", 7, rollback ? "Agendamento atualizado" : "Seu pedido já foi salvo");
        await f.HandleAgainAsync(2);
        Assert.Equal(2, f.Transport.Replies.Count);
        await using var saved = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, await saved.TelegramBusinessOperations.CountAsync());
    }

    private static async Task CreateJoaoAsync(Fixture f)
    {
        f.Model.InterpretationOverride = new(DialogueGoal.CriarAgendamento, DialogueReply.Resultado);
        f.Model.Tool("criar_agendamento", new { nomeCliente = "João", telefoneCliente = "11999999999", tipoServicoId = 2,
            data = "2030-01-07", enderecoServico = "Rua Inicial, 10", observacoes = "Armário" });
        await f.SendAsync(1, "Agende a montagem do João em 07/01/2030");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        f.Model.InterpretationOverride = null;
    }

    private static async Task AssertEditedAsync(Fixture f, int duration, int version, string address, int day, string heading = "Agendamento atualizado")
    {
        Assert.Contains(heading, f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        var saved = await db.Agendamentos.SingleAsync();
        Assert.Equal(duration, saved.Duracao);
        Assert.Equal(version, saved.Versao);
        Assert.Equal(address, saved.EnderecoSnapshot);
        Assert.Equal(day, saved.InicioPrevistoUtc.Day);
        Assert.Equal("Armário", saved.Observacoes);
        Assert.Equal(duration, await db.PeriodosOcupacao.CountAsync());
    }
}
