using System.Net;
using System.Text;
using System.Text.Json;
using Ian.Application.AI;
using Ian.Infrastructure.AI;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class GeminiDialogueContractTests
{
    [Fact]
    public async Task ConversaUsaSdkSemSchemaObrigatorioESemSegundaChamada()
    {
        var handler = new ContractHttp();
        using var http = new HttpClient(handler);
        var options = Options.Create(new GeminiOptions { ApiKey = "synthetic-key", Model = "gemini-3.1-flash-lite" });
        using var factory = new GeminiChatClientFactory(options, http);
        var registry = new AgentToolRegistry(new Backend(), Options.Create(new TelegramOptions { AdministratorUserId = 42 }), new AgentExecutionBudget());
        var result = await new GeminiAgendaAgentRunner(options, factory.GetClient, registry).RunAsync("Olá", new(42,42,true,true,true));
        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("Estou por aqui", AgentDialoguePlan.Parse(result.Text)?.Texto);
    }
    private sealed class Backend : IAgentToolBackend
    {
        public Task<AgentToolResult> ExecuteAsync(AgentToolRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentToolResult.Missing("Como posso ajudar com sua agenda?"));
    }
    private sealed class ContractHttp : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Calls++;
            var config = json.RootElement.GetProperty("generationConfig");
            Assert.False(config.TryGetProperty("responseJsonSchema", out _));
            Assert.False(config.TryGetProperty("responseMimeType", out _));
            Assert.Equal(2048, config.GetProperty("maxOutputTokens").GetInt32());
            Assert.Equal("MINIMAL", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
            Assert.False(json.RootElement.TryGetProperty("tools", out _));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                candidates = new[] { new { content = new { role = "model", parts = new[] { new { text = "{\"objetivo\":\"Conversa\",\"resposta\":\"Esclarecer\",\"texto\":\"Oi! Estou por aqui, como posso ajudar?\"}" } } }, finishReason = "STOP" } }
            }), Encoding.UTF8,"application/json") };
        }
    }
}
