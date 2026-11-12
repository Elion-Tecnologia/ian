using Google.GenAI;
using Ian.Application.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.AI;

public sealed class GeminiAgendaAgentRunner(
    IOptions<GeminiOptions> options,
    Func<IChatClient> chatClientFactory,
    IAgentToolRegistry registry) : IAgendaAgentRunner
{
    public async Task<AiModelResult> RunAsync(string prompt, AgentToolCaller caller, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > AgentExecutionLimits.MaxPromptCharacters || prompt.Contains('\0'))
            return AiModelResult.Failed(AiModelFailure.InvalidPrompt);
        if (!options.Value.IsValid()) return AiModelResult.Failed(AiModelFailure.InvalidConfiguration);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(options.Value.TimeoutSeconds, AgentExecutionLimits.TimeoutSeconds)));
        try
        {
            var client = new FunctionInvokingChatClient(chatClientFactory())
            {
                AllowConcurrentInvocation = false,
                IncludeDetailedErrors = false,
                MaximumIterationsPerRequest = AgentExecutionLimits.MaxTurns
            };
            var agent = new ChatClientAgent(client, new ChatClientAgentOptions
            {
                Name = "IanAgenda",
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions
                {
                    ModelId = options.Value.Model,
                    Instructions = AgentToolPolicy.SystemInstructions + " Use as Tools mesmo com argumentos parciais para persistir o pedido. Leia o contexto pendente e envie somente os novos dados coletados. Use listar_servicos para obter IDs e padrões. Nunca escolha entre candidatos, aceite confirmação ou substitua uma intenção pendente por conta própria. Datas relativas usam o dia e fuso informados pelo host. Não invente telefone, endereço, data nem horário. No máximo uma operação de escrita por update.",
                    Tools = registry.CreateTools(caller).Cast<AITool>().ToList(),
                    ToolMode = ChatToolMode.RequireAny,
                    MaxOutputTokens = 2048,
                    AllowMultipleToolCalls = false
                }
            });
            var response = await agent.RunAsync(prompt, cancellationToken: timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return AiModelResult.Succeeded(response.Text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (OperationCanceledException) { return AiModelResult.Failed(AiModelFailure.Timeout); }
        catch (ApiException ex) { return AiModelResult.Failed(GeminiAiModel.ClassifyStatus(ex.StatusCode)); }
        catch (HttpRequestException ex) { return AiModelResult.Failed(ex.StatusCode is { } s ? GeminiAiModel.ClassifyStatus((int)s) : AiModelFailure.ProviderUnavailable); }
        catch (Exception) { return AiModelResult.Failed(AiModelFailure.UnexpectedFailure); }
    }
}
