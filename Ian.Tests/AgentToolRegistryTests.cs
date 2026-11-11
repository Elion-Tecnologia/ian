using Ian.Application.AI;
using Ian.Infrastructure.AI;
using Ian.Infrastructure;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Ian.Tests;

public sealed class AgentToolRegistryTests
{
    [Fact]
    public void InfrastructureRegistersScopedToolContractsWithoutCallingGemini()
    {
        var configuration = new ConfigurationManager
        {
            ["Telegram:BotToken"] = "synthetic-token",
            ["Telegram:AdministratorUserId"] = "42",
            ["Telegram:WebhookSecret"] = "SYNTHETIC_SECRET",
            ["ConnectionStrings:Default"] = "Host=localhost;Database=ian_t01_tests;Username=test;Password=test"
        };
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>();
        var tools = registry.CreateTools(new AgentToolCaller(42, 42, true, true, true));

        Assert.Equal(5, tools.Count);
        Assert.Equal(new[] { "cadastrar_cliente", "consultar_agenda", "verificar_disponibilidade", "criar_agendamento", "alterar_agendamento" },
            tools.Select(tool => tool.Name));
        Assert.IsType<GeminiAiModel>(provider.GetRequiredService<IAiModel>());
    }

    [Fact]
    public async Task MissingArgumentsAndUnauthorizedCallerNeverReachBackend()
    {
        var backend = new RecordingBackend();
        var registry = new AgentToolRegistry(backend, Options.Create(new TelegramOptions { AdministratorUserId = 42 }), new AgentExecutionBudget());
        var tool = registry.CreateTools(new AgentToolCaller(42, 42, true, true, true)).Single(x => x.Name == "cadastrar_cliente");
        var missing = await tool.InvokeAsync(new AIFunctionArguments { ["nome"] = "Ana" });
        Assert.Equal(AgentToolOutcome.MissingData, ReadResult(missing).Outcome);
        Assert.Equal(0, backend.Calls);

        var unauthorizedTool = registry.CreateTools(new AgentToolCaller(43, 43, true, true, true)).Single(x => x.Name == "cadastrar_cliente");
        var denied = await unauthorizedTool.InvokeAsync(new AIFunctionArguments { ["nome"] = "Ana", ["telefone"] = "11999999999" });
        Assert.Equal(AgentToolOutcome.Error, ReadResult(denied).Outcome);
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task InvalidArgumentsFailClosedAndValidCallReturnsTypedBackendOutcome()
    {
        var backend = new RecordingBackend();
        var registry = new AgentToolRegistry(backend, Options.Create(new TelegramOptions { AdministratorUserId = 42 }), new AgentExecutionBudget());
        var tool = registry.CreateTools(new AgentToolCaller(42, 42, true, true, true)).Single(x => x.Name == "criar_agendamento");

        var invalid = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["nomeCliente"] = "Ana", ["telefoneCliente"] = "11999999999", ["tipoServicoId"] = 0L,
            ["data"] = new DateOnly(2026, 11, 12), ["modalidade"] = "NaLoja", ["duracao"] = 2,
            ["unidadeDuracao"] = "DiasCorridos"
        });
        Assert.Equal(AgentToolOutcome.Error, ReadResult(invalid).Outcome);
        Assert.Equal(0, backend.Calls);

        backend.Result = AgentToolResult.RequiresConfirmation("Confirme a operação.");
        var valid = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["nomeCliente"] = "Ana", ["telefoneCliente"] = "11999999999", ["tipoServicoId"] = 1L,
            ["data"] = new DateOnly(2026, 11, 12), ["modalidade"] = "NaLoja", ["duracao"] = 2,
            ["unidadeDuracao"] = "DiasCorridos"
        });
        Assert.Equal(AgentToolOutcome.ConfirmationRequired, ReadResult(valid).Outcome);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public void ResultTranslatorIsSafeAndBounded()
    {
        Assert.Equal("Não foi possível concluir a solicitação.", AgentToolResultTranslator.ToDialogue(null));
        var result = AgentToolResultTranslator.ToDialogue(AgentToolResult.Failed(new string('x', 20_000)));
        Assert.Equal("Não foi possível concluir a solicitação.", result);
    }

    [Fact]
    public async Task ToolCallBudgetStopsFurtherBackendCalls()
    {
        var backend = new RecordingBackend();
        var budget = new AgentExecutionBudget();
        var registry = new AgentToolRegistry(backend, Options.Create(new TelegramOptions { AdministratorUserId = 42 }), budget);
        var tool = registry.CreateTools(new AgentToolCaller(42, 42, true, true, true)).Single(x => x.Name == "cadastrar_cliente");
        var args = new AIFunctionArguments { ["nome"] = "Ana", ["telefone"] = "11999999999" };

        AgentToolResult? last = null;
        for (var call = 0; call < AgentExecutionLimits.MaxToolCalls + 1; call++)
            last = ReadResult(await tool.InvokeAsync(args));

        Assert.Equal(AgentExecutionLimits.MaxToolCalls, backend.Calls);
        Assert.Equal(AgentToolOutcome.Error, last!.Outcome);
        Assert.Contains("Limite", last.Message);
        for (var turn = 0; turn < AgentExecutionLimits.MaxTurns; turn++)
            Assert.True(budget.TryStartTurn());
        Assert.False(budget.TryStartTurn());
    }

    [Fact]
    public async Task DefaultBackendNeverClaimsBusinessOperationSucceeded()
    {
        var result = await new AgentToolBackend().ExecuteAsync(new CadastrarClienteToolRequest("Ana", "11999999999", null));
        Assert.Equal(AgentToolOutcome.Error, result.Outcome);
    }

    private sealed class RecordingBackend : IAgentToolBackend
    {
        public int Calls { get; private set; }
        public AgentToolResult Result { get; set; } = AgentToolResult.Succeeded("ok");
        public Task<AgentToolResult> ExecuteAsync(AgentToolRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private static AgentToolResult ReadResult(object? value) => value switch
    {
        AgentToolResult result => result,
        JsonElement json => FromJson(json),
        _ => throw new InvalidOperationException($"Unexpected result type: {value?.GetType().Name}")
    };

    private static AgentToolResult FromJson(JsonElement json)
    {
        var status = json.GetProperty("status").GetString();
        var message = json.GetProperty("message").GetString() ?? string.Empty;
        return Enum.Parse<AgentToolOutcome>(status!) switch
        {
            AgentToolOutcome.Success => AgentToolResult.Succeeded(message),
            AgentToolOutcome.MissingData => AgentToolResult.Missing(message),
            AgentToolOutcome.Ambiguous => AgentToolResult.Ambiguity(message),
            AgentToolOutcome.Conflict => AgentToolResult.ConflictResult(message),
            AgentToolOutcome.ConfirmationRequired => AgentToolResult.RequiresConfirmation(message),
            _ => AgentToolResult.Failed(message)
        };
    }
}
