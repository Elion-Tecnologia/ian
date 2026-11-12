using Google.GenAI;
using Google.GenAI.Types;
using Ian.Application.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ian.Infrastructure.AI;

public sealed class GeminiAgendaAgentRunner(
    IOptions<GeminiOptions> options,
    Func<IChatClient> chatClientFactory,
    IAgentToolRegistry registry,
    AgentTurnContext? context = null,
    ILogger<GeminiAgendaAgentRunner>? logger = null) : IAgendaAgentRunner
{
    public async Task<AiModelResult> RunAsync(string prompt, AgentToolCaller caller, CancellationToken cancellationToken = default)
    {
        var phase = "validation";
        AiModelResult Fail(AiModelFailure failure, int? status = null, string? exceptionType = null)
        {
            logger?.LogWarning("Update {UpdateId} stage=model_failed outcome={Outcome} phase={Phase} provider_status={ProviderStatus} exception_type={ExceptionType}.",
                context?.UpdateId, failure, phase, status, exceptionType);
            return AiModelResult.Failed(failure);
        }
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > AgentExecutionLimits.MaxPromptCharacters || prompt.Contains('\0'))
            return Fail(AiModelFailure.InvalidPrompt);
        if (!options.Value.IsValid()) return Fail(AiModelFailure.InvalidConfiguration);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(options.Value.TimeoutSeconds, AgentExecutionLimits.TimeoutSeconds)));
        try
        {
            phase = "interpretation";
            logger?.LogInformation("Update {UpdateId} stage=model_interpret_started.", context?.UpdateId);
            var transport = chatClientFactory();
            var interpretation = await transport.GetResponseAsync(
                [new ChatMessage(ChatRole.System, AgentDialoguePlan.Instructions), new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { ModelId = options.Value.Model, ToolMode = ChatToolMode.None, MaxOutputTokens = 2048,
                    RawRepresentationFactory = _ => new GenerateContentConfig
                    {
                        ThinkingConfig = options.Value.Model.StartsWith("gemini-3.1-flash-lite", StringComparison.Ordinal)
                            ? new ThinkingConfig { ThinkingLevel = ThinkingLevel.Minimal } : null
                    }
                }, timeout.Token);
            logger?.LogInformation("Update {UpdateId} stage=model_interpret_received finish={Finish} output_tokens={OutputTokens}.",
                context?.UpdateId, Finish(interpretation.FinishReason), interpretation.Usage?.OutputTokenCount);
            var plan = interpretation.FinishReason is { } reason && reason != ChatFinishReason.Stop ||
                interpretation.Messages.SelectMany(m => m.Contents).Any(c => c is FunctionCallContent)
                ? null : AgentDialoguePlan.Parse(interpretation.Text) ??
                    (AgentResponseComposer.IsSafeNaturalText(interpretation.Text)
                        ? new AgentDialoguePlan(DialogueGoal.Conversa, DialogueReply.Esclarecer) { Texto = interpretation.Text } : null);
            if (plan is null)
            {
                logger?.LogWarning("Update {UpdateId} stage=model_interpret_invalid.", context?.UpdateId);
                return Fail(AiModelFailure.InvalidResponse);
            }
            logger?.LogInformation("Update {UpdateId} stage=model_interpreted goal={Goal} reply={Reply}.", context?.UpdateId, plan.Objetivo, plan.Resposta);
            phase = "begin_turn";
            await registry.BeginTurnAsync(plan, caller, timeout.Token);
            if (plan.Objetivo == DialogueGoal.Conversa && AgentResponseComposer.IsSafeNaturalText(plan.Texto))
            {
                logger?.LogInformation("Update {UpdateId} stage=model_dialogue_completed outcome=SocialDirect.", context?.UpdateId);
                return AiModelResult.Succeeded(JsonSerializer.Serialize(plan, AgentRequestJson.Options));
            }
            plan = plan with { Texto = null };
            phase = "tools";
            var client = new FunctionInvokingChatClient(transport)
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
                    Instructions = AgentToolPolicy.SystemInstructions + " Use as Tools mesmo com argumentos parciais. O objetivo foi identificado antes das Tools e deve ser preservado. Consulte o catálogo quando necessário e continue a operação, lendo os resultados das funções. Nunca escolha candidatos, aceite confirmação ou substitua intenção pendente por conta própria. Datas relativas usam o dia/fuso do host. Não invente telefone, endereço, data ou horário. No máximo uma escrita por update. Para o ato final de agenda gere JSON com objetivo, resposta e texto; para conversa pode retornar texto natural. " + AgentDialoguePlan.Instructions + " " + AgentResponseComposer.Instructions,
                    Tools = registry.CreateTools(caller).Cast<AITool>().ToList(),
                    ToolMode = ChatToolMode.Auto,
                    MaxOutputTokens = 2048,
                    AllowMultipleToolCalls = false
                }
            });
            var response = await agent.RunAsync(prompt + "\nObjetivo interpretado (não é prova de execução): " + JsonSerializer.Serialize(plan, AgentRequestJson.Options), cancellationToken: timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            phase = "composition";
            logger?.LogInformation("Update {UpdateId} stage=model_final_received finish={Finish}.", context?.UpdateId, Finish(response.FinishReason));
            if (response.FinishReason is { } finish && finish != ChatFinishReason.Stop)
                return Fail(AiModelFailure.InvalidResponse);
            var reply = AgentDialoguePlan.Parse(response.Text);
            if (reply is null && AgentResponseComposer.IsSafeNaturalText(response.Text)) reply = plan with { Texto = response.Text };
            if (reply is null)
                logger?.LogWarning("Update {UpdateId} stage=model_response_invalid phase={Phase}.", context?.UpdateId, phase);
            logger?.LogInformation("Update {UpdateId} stage=model_dialogue_completed outcome={Outcome}.", context?.UpdateId, reply is null ? "InvalidActFallback" : "ValidAct");
            // Unstructured model prose can never attest an operation. Preserve the interpreted goal.
            return AiModelResult.Succeeded(JsonSerializer.Serialize((reply ?? plan) with { Objetivo = plan.Objetivo }, AgentRequestJson.Options));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (OperationCanceledException) { return Fail(AiModelFailure.Timeout); }
        catch (ApiException ex) { return Fail(GeminiAiModel.ClassifyStatus(ex.StatusCode), ex.StatusCode, nameof(ApiException)); }
        catch (HttpRequestException ex) { return Fail(ex.StatusCode is { } s ? GeminiAiModel.ClassifyStatus((int)s) : AiModelFailure.ProviderUnavailable,
            ex.StatusCode is { } code ? (int)code : null, nameof(HttpRequestException)); }
        catch (JsonException) { return Fail(AiModelFailure.InvalidResponse, exceptionType: nameof(JsonException)); }
        catch (Exception ex) { return Fail(AiModelFailure.UnexpectedFailure, exceptionType: ex.GetType().Name); }
    }
    private static string Finish(ChatFinishReason? reason) => reason == ChatFinishReason.Stop ? "Stop"
        : reason == ChatFinishReason.Length ? "TokenLimit" : reason == ChatFinishReason.ToolCalls ? "ToolCalls" : "Other";

}
