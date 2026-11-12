using System.Net;
using System.Text;
using System.Text.Json;
using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresTheory]
    [InlineData("Queria agendar um serviço")]
    [InlineData("Gostaria de marcar uma montagem")]
    [InlineData("Preciso reservar um serviço")]
    public async Task InterpretacaoIndependenteDaPrimeiraToolMantemObjetivoEExplicaColetaAposReinicio(string pedido)
    {
        await using var f = await Fixture.CreateAsync();
        var model = UseConversationSimulation(f);
        await f.SendAsync(1, pedido);
        var first = f.Transport.Replies[^1];
        Assert.Contains("nome do cliente", first);
        Assert.DoesNotContain("Serviços:", first);
        Assert.Equal(new[] { "listar_servicos" }, model.ExecutedTools); // Intentionally the same intermediate choice that escaped #96.
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            Assert.Contains("CriarAgendamento", (await db.SolicitacoesPendentes.SingleAsync()).Operacao);
            Assert.Empty(await db.Agendamentos.ToListAsync());
        }
        f.Restart();
        await f.SendAsync(2, "O que é isto?");
        var explanation = f.Transport.Replies[^1];
        Assert.Contains("próximo dado", explanation);
        Assert.DoesNotContain("Serviços:", explanation);
        Assert.DoesNotContain("Montagem", explanation);
        Assert.Contains("nome do cliente", explanation);
        Assert.DoesNotContain("Como posso ajudar", explanation);
        Assert.True(model.RecoveredHistory);
        Assert.Equal(new[] { "listar_servicos" }, model.ExecutedTools); // A contextual question needs no forced Tool call.
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Contains("CriarAgendamento", (await check.SolicitacoesPendentes.SingleAsync()).Operacao);
        Assert.Empty(await check.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task SimulacaoSdkLeResultadosSequenciaisEPersisteSomenteAposEsclarecimentoDoHorario()
    {
        await using var f = await Fixture.CreateAsync();
        var model = UseConversationSimulation(f);
        await f.SendAsync(1, "Queria agendar um serviço");
        await f.SendAsync(2, "Montagem para Ana em 07/01/2030, Rua Fictícia, às 10h.");
        Assert.True(model.ConsumedCatalog);
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.Contains("10:00", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(3, "O telefone é 11999999999.");
        Assert.Contains("08h", f.Transport.Replies[^1]);
        Assert.DoesNotContain("telefone", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext()) Assert.Empty(await db.Agendamentos.ToListAsync());
        f.Transport.BeforeReply = async _ =>
        {
            await using var db = PostgresTestDatabase.CreateContext();
            Assert.Single(await db.Agendamentos.ToListAsync());
        };
        await f.SendAsync(4, "Pode começar às 08h.");
        Assert.Contains("Agendamento criado", f.Transport.Replies[^1]);
        Assert.Contains("Montagem para Ana", f.Transport.Replies[^1]);
        Assert.Contains("07/01/2030 a 13/01/2030", f.Transport.Replies[^1]);
        await f.SendAsync(5, "Consulte a agenda de 09/01/2030.");
        Assert.Contains("Montagem", f.Transport.Replies[^1]);
        Assert.Contains("Ana", f.Transport.Replies[^1]);
        var calls = model.Calls;
        await f.EnqueueAsync(4, "Pode começar às 08h.");
        Assert.False(await f.ProcessAsync());
        await f.HandleAgainAsync(4);
        Assert.Equal(calls, model.Calls);
        Assert.Equal(5, f.Transport.Replies.Count);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
        Assert.Equal(7, await check.PeriodosOcupacao.CountAsync());
        AssertStage(f, 4, "webhook_persisted_or_duplicate", "inbox_claimed", "context_loaded", "model_interpreted", "tool_completed", "response_saved", "send_started", "send_finished", "inbox_completed", "send_skipped");
        var logs = string.Join("\n", f.Logs.Messages);
        foreach (var value in new[] { "Queria agendar", "Rua Fict", "11999999999", "synthetic-token", "synthetic-key", "synthetic-secret" })
            Assert.DoesNotContain(value, logs);
    }

    [PostgresTheory]
    [InlineData("empty")]
    [InlineData("invented_success")]
    [InlineData("final_invented_success")]
    [InlineData("provider_failure")]
    public async Task RespostaInvalidaOuFalhaDoModeloNaoSomeNemInventaEscrita(string failure)
    {
        await using var f = await Fixture.CreateAsync();
        var model = UseConversationSimulation(f);
        model.Failure = failure;
        await f.SendAsync(1, "Queria agendar um serviço");
        Assert.Single(f.Transport.Replies);
        Assert.DoesNotContain("Agendamento criado", f.Transport.Replies[^1]);
        Assert.DoesNotContain("PRIVATE_INVENTED_SUCCESS", f.Transport.Replies[^1]);
        AssertStage(f, 1, "response_saved", "send_finished", "inbox_completed");
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresTheory]
    [InlineData("rate", "Failed")]
    [InlineData("uncertain", "Uncertain")]
    public async Task EnvioSemRespostaVisivelTemEtapaECausaCorrelacionaveisSemReexecutarModelo(string failure, string state)
    {
        await using var f = await Fixture.CreateAsync();
        var model = UseConversationSimulation(f);
        f.Transport.Failure = failure;
        await f.SendAsync(1, "Queria agendar um serviço");
        Assert.Empty(f.Transport.Replies);
        AssertStage(f, 1, "webhook_persisted_or_duplicate", "inbox_claimed", "model_interpreted", "response_saved", "send_started", "send_finished state=" + state);
        var calls = model.Calls;
        f.Restart();
        f.Transport.Failure = null;
        if (failure == "rate")
        {
            await f.MakeRetryReadyAsync(1);
            Assert.True(await f.ProcessAsync());
            Assert.Single(f.Transport.Replies);
        }
        else
        {
            await f.HandleAgainAsync(1);
            Assert.Empty(f.Transport.Replies);
            AssertStage(f, 1, "send_skipped");
        }
        Assert.Equal(calls, model.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FalhaAntesOuDepoisDaMutacaoPreservaProvaDePersistencia(bool rollback)
    {
        await using var f = await Fixture.CreateAsync();
        var model = UseConversationSimulation(f);
        await f.SendAsync(1, "Queria agendar um serviço");
        await f.SendAsync(2, "Montagem para Ana em 07/01/2030, Rua Fictícia, às 10h.");
        await f.SendAsync(3, "O telefone é 11999999999.");
        f.Faults.FailCreation = rollback;
        model.Failure = "after_tool";
        await f.SendAsync(4, "Pode começar às 08h.");
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(rollback ? 0 : 1, await db.Agendamentos.CountAsync());
        Assert.Equal(rollback ? 0 : 1, await db.Clientes.CountAsync());
        Assert.Equal(!rollback, f.Transport.Replies[^1].Contains("Agendamento criado"));
        AssertStage(f, 4, "model_failed", "send_finished");
    }

    [PostgresFact]
    public async Task ToolDeEsclarecimentoNaoSubstituiExplicacaoContextualEscolhidaPeloModelo()
    {
        await using var f = await Fixture.CreateAsync();
        var model = UseConversationSimulation(f);
        await f.SendAsync(1, "Queria agendar um serviço");
        f.Restart();
        model.ClarificationTool = true;
        await f.SendAsync(2, "O que é isto?");
        Assert.Contains("esclarecer_pedido", model.ExecutedTools);
        Assert.Contains("próximo dado", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Serviços:", f.Transport.Replies[^1]);
        Assert.Contains("nome do cliente", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Como posso ajudar", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    private static ConversationSimulation UseConversationSimulation(Fixture f)
    {
        var model = new ConversationSimulation();
        f.Faults.UseSdk = true;
        f.Faults.GeminiTransport = () => new ConversationHttp(model);
        return model;
    }

    private static void AssertStage(Fixture f, long updateId, params string[] stages)
    {
        foreach (var stage in stages)
            Assert.Contains(f.Logs.Messages, log => log.Contains($"Update {updateId} stage={stage}"));
    }

    // A protocol simulation, not an evaluation of real Gemini's semantic quality. Decisions are
    // independent of Tool choice; subsequent calls must consume actual SDK function responses.
    private sealed class ConversationSimulation
    {
        public int Calls { get; set; }
        public bool RecoveredHistory { get; set; }
        public bool ConsumedCatalog { get; set; }
        public string? Failure { get; set; }
        public bool ClarificationTool { get; set; }
        public List<string> ExecutedTools { get; } = [];
    }

    private sealed class ConversationHttp(ConversationSimulation state) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri?.Host);
            Assert.Equal("synthetic-key", request.Headers.GetValues("x-goog-api-key").Single());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            state.Calls++;
            var parts = root.GetProperty("contents").EnumerateArray().SelectMany(c => c.GetProperty("parts").EnumerateArray()).ToArray();
            var prompt = string.Join("\n", parts.Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString()));
            var text = prompt.Split("Texto da administradora: ")[^1].Split("\nObjetivo interpretado")[0];
            var feedback = parts.Where(p => p.TryGetProperty("functionResponse", out _)).Select(p => p.GetProperty("functionResponse")).ToArray();
            var planning = !root.TryGetProperty("tools", out _);
            if (state.Failure == "provider_failure" || state.Failure == "after_tool" && feedback.Length > 0)
                return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
            if (planning)
            {
                Assert.False(root.GetProperty("generationConfig").TryGetProperty("responseJsonSchema", out _));
                Assert.True(root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32() >= 2048);
                if (state.Failure == "invented_success") return Text("PRIVATE_INVENTED_SUCCESS: Agendamento criado.");
                var goal = text.Contains("Queria") || text.Contains("Gostaria") || text.Contains("Preciso") ? "CriarAgendamento" : "Continuar";
                var reply = text.Contains("isto") ? "ExplicarCatalogo" : text.Contains("Consulte") ? "Resultado" : "Coletar";
                if (text.Contains("isto"))
                {
                    state.RecoveredHistory = prompt.Contains("Histórico recente") && prompt.Contains("nome do cliente") && prompt.Contains("CriarAgendamento");
                    Assert.True(state.RecoveredHistory);
                }
                return Plan(goal, reply);
            }
            Assert.NotEqual("ANY", root.TryGetProperty("toolConfig", out var config) ? config.GetProperty("functionCallingConfig").GetProperty("mode").GetString() : "AUTO");
            if (text.Contains("isto")) return state.ClarificationTool && feedback.Length == 0
                ? Call("esclarecer_pedido", new { }) : Plan("Continuar", "ExplicarCatalogo");
            if (feedback.Length == 0)
            {
                if (text.Contains("telefone")) return Call("localizar_cliente", new { telefone = "11999999999" });
                if (text.Contains("08h")) return Call("criar_agendamento", new { horario = "08:00" });
                if (text.Contains("Consulte")) return Call("consultar_agenda", new { dataInicial = "2030-01-09" });
                return Call("listar_servicos", new { });
            }
            var latest = feedback[^1];
            if (latest.GetProperty("name").GetString() == "listar_servicos" && text.Contains("Montagem para Ana"))
            {
                var data = Find(latest.GetProperty("response"), "data");
                var montagem = data.EnumerateArray().Single(t => t.GetProperty("nome").GetString() == "Montagem");
                Assert.Equal("DiasCorridos", montagem.GetProperty("unidadeDuracaoPadrao").GetString());
                state.ConsumedCatalog = true;
                return Call("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = montagem.GetProperty("id").GetInt64(),
                    data = "2030-01-07", horario = "10:00", enderecoServico = "Rua Fictícia" });
            }
            if (state.Failure == "empty") return Text("");
            if (state.Failure == "final_invented_success") return Text("PRIVATE_INVENTED_SUCCESS: Agendamento criado.");
            return Plan("Continuar", text.Contains("Consulte") ? "Resultado" : "Coletar");
        }

        private HttpResponseMessage Call(string name, object args)
        {
            state.ExecutedTools.Add(name);
            return Response(new { functionCall = new { name, args } });
        }
        private static HttpResponseMessage Plan(string goal, string reply) => Text(JsonSerializer.Serialize(new { objetivo = goal, resposta = reply }));
        private static HttpResponseMessage Text(string text) => Response(new { text });
        private static HttpResponseMessage Response(object part) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                candidates = new[] { new { content = new { role = "model", parts = new[] { part } }, finishReason = "STOP" } }
            }), Encoding.UTF8, "application/json")
        };
        private static JsonElement Find(JsonElement node, string field)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty(field, out var value)) return value;
                foreach (var item in node.EnumerateObject())
                {
                    var found = Find(item.Value, field);
                    if (found.ValueKind != JsonValueKind.Undefined) return found;
                }
            }
            return default;
        }
    }
}
