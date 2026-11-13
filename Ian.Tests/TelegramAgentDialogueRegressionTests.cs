using System.Net;
using System.Text;
using System.Text.Json;
using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresFact]
    public async Task SequenciaRealDaIssue103ConversaConsultaEColetaContinuamAposReinicio()
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(new DateTimeOffset(2030, 1, 7, 13, 30, 0, TimeSpan.Zero)); // 10:30 São Paulo.
        var simulation = UseIssue103(f);
        foreach (var (id, text) in new[] { (1L, "Olá"), (2L, "Oi IAN tudo bem?"), (3L, "Tá aí?") })
        {
            await f.SendAsync(id, text);
            Assert.DoesNotContain("Não foi possível", f.Transport.Replies[^1]);
            Assert.DoesNotContain("consultar ou organizar", f.Transport.Replies[^1]);
        }
        Assert.Contains("Olá", f.Transport.Replies[0]);
        Assert.Contains("bem", f.Transport.Replies[1]);
        Assert.Contains("aqui", f.Transport.Replies[2]);
        Assert.Equal(3, simulation.Calls);
        Assert.Empty(simulation.Tools);
        await f.SendAsync(4, "Tenho algum serviço hoje?");
        Assert.Contains("Nenhum agendamento", f.Transport.Replies[^1]);
        Assert.Equal("consultar_agenda", Assert.Single(simulation.Tools));
        await f.SendAsync(5, "Quero agendar pro restante do dia de hoje");
        Assert.Contains("cliente", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Não foi possível", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var pending = await db.SolicitacoesPendentes.SingleAsync();
            Assert.Contains("CriarAgendamento", pending.Operacao);
            Assert.Contains("2030-01-07", pending.DadosColetadosJson);
            Assert.Contains("periodoRestante", pending.DadosColetadosJson);
            Assert.Empty(await db.Agendamentos.ToListAsync());
        }
        f.Restart();
        await f.SendAsync(6, "Visita para Ana, telefone 11999999999, na Rua Fictícia.");
        Assert.Contains("horário", f.Transport.Replies[^1]);
        Assert.DoesNotContain("telefone", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Empty(await db.Agendamentos.ToListAsync());
            Assert.Contains("dura", (await db.SolicitacoesPendentes.SingleAsync()).CamposFaltantesJson);
        }
        f.Restart();
        await f.SendAsync(7, "Pode começar às 11h.");
        Assert.Contains("tempo", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome", f.Transport.Replies[^1]);
        await f.SendAsync(8, "Duas horas, em minutos.");
        var receipt = f.Transport.Replies[^1];
        Assert.Contains("Agendamento criado", receipt);
        Assert.Contains("Ana", receipt);
        Assert.Contains("07/01/2030", receipt);
        Assert.Contains("11:00", receipt);
        Assert.Contains("#1", receipt);
        Assert.DoesNotContain("Maria", receipt);
        Assert.Equal(17, simulation.Calls); // Structured interpretation and Tool collection preserve the pending request.
        var calls = simulation.Calls;
        f.Restart();
        await f.HandleAgainAsync(8);
        Assert.Equal(calls, simulation.Calls);
        Assert.Equal(8, f.Transport.Replies.Count);
        await using var check = PostgresTestDatabase.CreateContext();
        var appointment = await check.Agendamentos.SingleAsync();
        Assert.Equal(120, appointment.Duracao);
        Assert.Equal(UnidadeDuracao.Minutos, appointment.UnidadeDuracao);
        Assert.Single(await check.Clientes.ToListAsync());
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
        var logs = string.Join("\n", f.Logs.Messages);
        foreach (var value in new[] { "Rua Fictícia", "11999999999", "Oi IAN", "synthetic-key", "synthetic-token", "PRIVATE_" })
            Assert.DoesNotContain(value, logs);
        AssertStage(f, 1, "model_dialogue_completed outcome=SocialDirect", "response_composed");
    }

    [PostgresFact]
    public async Task InterpretacaoEstruturadaPreservaIntencaoExplicitaENaoInventaDuracao()
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(new DateTimeOffset(2030, 1, 8, 1, 30, 0, TimeSpan.Zero));
        var simulation = UseIssue103(f);
        simulation.RawCreationQuestion = true;
        await f.SendAsync(1, "Quero agendar pro restante do dia de hoje");
        Assert.Contains("cliente", f.Transport.Replies[^1]);
        Assert.Equal(3, simulation.Calls);
        Assert.Equal(new[] { "criar_agendamento" }, simulation.Tools);
        await using var db = PostgresTestDatabase.CreateContext();
        var pending = await db.SolicitacoesPendentes.SingleAsync();
        Assert.Contains("CriarAgendamento", pending.Operacao);
        Assert.Contains("2030-01-07", pending.DadosColetadosJson);
        Assert.Contains("periodoRestante", pending.DadosColetadosJson);
        Assert.Empty(await db.Agendamentos.ToListAsync());
        AssertStage(f, 1, "intent_recovered");
    }

    [PostgresFact]
    public async Task ReciboLongoMantemReferenciaExataAntesDoLimiteDoTelegram()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 2L,
            data = "2030-01-07", enderecoServico = new string('A', 2000), observacoes = new string('B', 2000) });
        await f.SendAsync(1, "Monte o serviço conforme os dados fornecidos.");
        var receipt = Assert.Single(f.Transport.Replies);
        Assert.True(receipt.Length <= 4000);
        Assert.Contains("Agendamento criado (#1)", receipt);
        Assert.Contains("Ana", receipt);
        Assert.Contains("07/01/2030", receipt);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("provider", "ProviderUnavailable", "ModelFailure")]
    [InlineData("invalid", "InvalidResponse", "ModelInvalidResponse")]
    [InlineData("limit", "InvalidResponse", "ModelInvalidResponse")]
    [InlineData("timeout", "Timeout", "ModelTimeout")]
    public async Task FalhasAntesDasToolsSaoDistintasERetomamPedidoSemEscrita(string failure, string outcome, string category)
    {
        await using var f = await Fixture.CreateAsync();
        f.Clock.Set(new DateTimeOffset(2030, 1, 8, 1, 30, 0, TimeSpan.Zero)); // Still January 7 locally.
        var simulation = UseIssue103(f);
        simulation.Failure = failure;
        await f.SendAsync(1, "Tá aí?");
        Assert.DoesNotContain("Não foi possível concluir a solicitação", f.Transport.Replies[^1]);
        Assert.DoesNotContain("PRIVATE_", f.Transport.Replies[^1]);
        await f.SendAsync(2, "Quero agendar pro restante do dia de hoje");
        Assert.Contains("cliente", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(3, "Pode continuar?");
        Assert.Contains("cliente", f.Transport.Replies[^1]);
        Assert.Empty(simulation.Tools);
        var logs = string.Join("\n", f.Logs.Messages);
        Assert.Contains("stage=model_failed outcome=" + outcome + " phase=interpretation", logs);
        Assert.Contains("stage=response_fallback category=" + category, logs);
        if (failure == "provider") Assert.Contains("provider_status=503", logs);
        if (failure == "limit") Assert.Contains("finish=TokenLimit", logs);
        Assert.DoesNotContain("PRIVATE_", logs);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Contains("2030-01-07", (await db.SolicitacoesPendentes.SingleAsync()).DadosColetadosJson);
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task FalhaDeToolTemCategoriaPropriaSemMascararComoFalhaDeModelo()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.FailCreation = true;
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(1, "Crie montagem para Ana.");
        Assert.Contains("Consulte a agenda", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains(f.Logs.Messages, log => log.Contains("stage=tool_completed") && log.Contains("category=ToolFailure"));
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    private static Issue103Simulation UseIssue103(Fixture f)
    {
        var state = new Issue103Simulation();
        f.Faults.UseSdk = true;
        f.Faults.GeminiTransport = () => new Issue103Http(state);
        return state;
    }
    private sealed class Issue103Simulation
    {
        public int Calls { get; set; }
        public string? Failure { get; set; }
        public bool RawCreationQuestion { get; set; }
        public List<string> Tools { get; } = [];
    }
    private sealed class Issue103Http(Issue103Simulation state) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri?.Host);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = document.RootElement;
            state.Calls++;
            var parts = root.GetProperty("contents").EnumerateArray().SelectMany(c => c.GetProperty("parts").EnumerateArray()).ToArray();
            var prompt = string.Join("\n", parts.Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString()));
            var text = prompt.Split("Texto da administradora: ")[^1].Split("\nObjetivo interpretado")[0];
            var feedback = parts.Where(p => p.TryGetProperty("functionResponse", out _)).Select(p => p.GetProperty("functionResponse")).ToArray();
            if (!root.TryGetProperty("tools", out _))
            {
                var config = root.GetProperty("generationConfig");
                Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
                Assert.True(config.TryGetProperty("responseJsonSchema", out _));
                Assert.Equal("MINIMAL", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
                if (state.Failure == "provider") return new(HttpStatusCode.ServiceUnavailable)
                    { Content = new StringContent("{\"error\":{\"code\":503,\"message\":\"PRIVATE_PROVIDER_BODY\",\"status\":\"UNAVAILABLE\"}}") };
                if (state.Failure == "timeout") throw new OperationCanceledException("PRIVATE_TIMEOUT");
                if (state.Failure == "invalid") return Text("{invalid PRIVATE_JSON}");
                if (state.Failure == "limit") return Text("", "MAX_TOKENS");
                if (state.RawCreationQuestion && text.Contains("Quero agendar"))
                    return Dialogue(new AgentDialoguePlan(DialogueGoal.CriarAgendamento, DialogueReply.Coletar));
                if (text == "Olá") return Dialogue(new AgentDialoguePlan(DialogueGoal.Conversa, DialogueReply.Esclarecer) { Texto = "Olá! Como posso ajudar?" });
                if (text == "Oi IAN tudo bem?") return Dialogue(new AgentDialoguePlan(DialogueGoal.Conversa, DialogueReply.Esclarecer) { Texto = "Tudo bem por aqui! E com você?" });
                if (text == "Tá aí?") return Dialogue(new AgentDialoguePlan(DialogueGoal.Conversa, DialogueReply.Esclarecer) { Texto = "Estou aqui, pode falar!" });
                return Dialogue(new AgentDialoguePlan(text.Contains("Quero agendar") ? DialogueGoal.CriarAgendamento
                    : text.Contains("Tenho") ? DialogueGoal.Consulta : DialogueGoal.Continuar, DialogueReply.Coletar));
            }
            if (feedback.Length == 0)
            {
                if (text.Contains("Tenho")) return Call("consultar_agenda", new { dataInicial = "2030-01-07" });
                if (text.Contains("Visita para Ana")) return Call("criar_agendamento", new { nomeCliente = "Ana", telefoneCliente = "11999999999", tipoServicoId = 1L, enderecoServico = "Rua Fictícia" });
                if (text.Contains("11h")) return Call("criar_agendamento", new { horario = "11:00" });
                if (text.Contains("Duas horas")) return Call("criar_agendamento", new { duracao = 120, unidadeDuracao = "Minutos" });
                return Call("criar_agendamento", new { });
            }
            var topic = Find(feedback[^1], "questionTopic");
            return Text(topic.ValueKind == JsonValueKind.String ? topic.GetString() switch
            {
                "nome" => "Como se chama o cliente?",
                "horário" => "Em qual horário você gostaria de começar?",
                "duração" => "Por quanto tempo vamos precisar do serviço?",
                _ => "Pode me contar um pouco mais?"
            } : "Agendei para Maria amanhã."); // Protected result must override fabricated final prose.
        }
        private HttpResponseMessage Call(string name, object args)
        {
            state.Tools.Add(name);
            return Response(new { functionCall = new { name, args } });
        }
        private static HttpResponseMessage Dialogue(AgentDialoguePlan plan) => Text(JsonSerializer.Serialize(plan, AgentRequestJson.Options));
        private static HttpResponseMessage Text(string text, string reason = "STOP") => Response(new { text }, reason);
        private static HttpResponseMessage Response(object part, string reason = "STOP") => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                candidates = new[] { new { content = new { role = "model", parts = new[] { part } }, finishReason = reason } }
            }), Encoding.UTF8, "application/json")
        };
        private static JsonElement Find(JsonElement node, string name)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty(name, out var value)) return value;
                foreach (var item in node.EnumerateObject())
                {
                    var result = Find(item.Value, name);
                    if (result.ValueKind != JsonValueKind.Undefined) return result;
                }
            }
            return default;
        }
    }
}
