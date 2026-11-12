using Ian.Application.AI;
using Ian.Infrastructure.AI;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class GeminiAgendaAgentRunnerTests
{
    [Fact]
    public async Task CicloDeFunctionCallingTemLimiteDeIteracoesEChamadas()
    {
        var client = new RepeatingClient("listar_servicos");
        var backend = new Backend();
        var result = await Runner(client, backend).RunAsync("Liste os serviços.", Caller());
        Assert.InRange(client.Calls, 1, AgentExecutionLimits.MaxTurns + 2);
        Assert.InRange(backend.Calls, 1, AgentExecutionLimits.MaxToolCalls);
        Assert.False(result.IsSuccess); // The looping fake never returns final text.
    }

    [Fact]
    public async Task FuncaoNaoRegistradaNuncaAcessaBackend()
    {
        var client = new RepeatingClient("executar_sql");
        var backend = new Backend();
        await Runner(client, backend).RunAsync("Instrução sintética.", Caller());
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task PromptExcedidoNaoInvocaTransporte()
    {
        var client = new RepeatingClient("listar_servicos");
        var result = await Runner(client, new Backend()).RunAsync(new string('x', 16001), Caller());
        Assert.Equal(AiModelFailure.InvalidPrompt, result.Failure);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task CancelamentoDoHostEhPropagadoSemResultadoDeSucesso()
    {
        var client = new RepeatingClient("listar_servicos");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(client, new Backend()).RunAsync("Consulta.", Caller(), cancellation.Token));
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task PrazoDoProvedorUsaFallbackSemInvocarTools()
    {
        var backend = new Backend();
        var runner = new GeminiAgendaAgentRunner(
            Options.Create(new GeminiOptions { ApiKey = "synthetic-key", Model = "gemini-3.1-flash-lite", TimeoutSeconds = 1 }),
            () => new SlowClient(),
            new AgentToolRegistry(backend, Options.Create(new TelegramOptions { AdministratorUserId = 42 }), new AgentExecutionBudget()));
        var result = await runner.RunAsync("Consulta sintética.", Caller());
        Assert.Equal(AiModelFailure.Timeout, result.Failure);
        Assert.Equal(0, backend.Calls);
    }

    private sealed class SlowClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Deadline was not enforced.");
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static AgentToolCaller Caller() => new(42, 42, true, true, true);
    private static GeminiAgendaAgentRunner Runner(IChatClient client, Backend backend) => new(
        Options.Create(new GeminiOptions { ApiKey = "synthetic-key", Model = "gemini-3.1-flash-lite" }), () => client,
        new AgentToolRegistry(backend, Options.Create(new TelegramOptions { AdministratorUserId = 42 }), new AgentExecutionBudget()));
    private sealed class Backend : IAgentToolBackend
    {
        public int Calls { get; private set; }
        public Task<AgentToolResult> ExecuteAsync(AgentToolRequest request, CancellationToken cancellationToken = default)
        {
            if (request is not InterpretarTurnoRequest) Calls++;
            return Task.FromResult(AgentToolResult.Succeeded("Serviços sintéticos."));
        }
    }
    private sealed class RepeatingClient(string name) : IChatClient
    {
        public int Calls { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (options?.ToolMode == ChatToolMode.None)
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"objetivo":"Consulta","resposta":"Resultado"}""")));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(Calls.ToString(), name, new Dictionary<string, object?>())])) { FinishReason = ChatFinishReason.ToolCalls });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
