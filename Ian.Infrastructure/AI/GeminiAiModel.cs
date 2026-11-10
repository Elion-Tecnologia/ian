using System.Text.Json;
using Google.GenAI;
using Ian.Application.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.AI;

public sealed class GeminiAiModel(
    IOptions<GeminiOptions> options,
    Func<IChatClient> chatClientFactory) : IAiModel
{
    public async Task<AiModelResult> InvokeAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 16_000 || prompt.Contains('\0'))
        {
            return AiModelResult.Failed(AiModelFailure.InvalidPrompt);
        }

        var settings = options.Value;
        if (!settings.IsValid())
        {
            return AiModelResult.Failed(AiModelFailure.InvalidConfiguration);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            // A new MAF agent/session for each call; no business tools are registered.
            var agent = new ChatClientAgent(chatClientFactory(), new ChatClientAgentOptions
            {
                Name = "IanTextModel",
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions
                {
                    ModelId = settings.Model,
                    ToolMode = ChatToolMode.None,
                    MaxOutputTokens = 2048
                }
            });
            var response = await agent.RunAsync(prompt, cancellationToken: timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(response.Text) ||
                response.FinishReason is { } finish && finish != ChatFinishReason.Stop ||
                response.Messages.Any(message => message.Role != ChatRole.Assistant ||
                    message.Contents.Any(content => content is not (TextContent or TextReasoningContent))))
            {
                return AiModelResult.Failed(AiModelFailure.InvalidResponse);
            }

            var usage = response.Usage;
            return AiModelResult.Succeeded(response.Text,
                usage is null ? null : new AiModelUsage(
                    usage.InputTokenCount, usage.OutputTokenCount, usage.TotalTokenCount));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Some providers wrap cancellation. Preserve host/lease cancellation semantics.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            return AiModelResult.Failed(AiModelFailure.Timeout);
        }
        catch (OperationCanceledException)
        {
            return AiModelResult.Failed(AiModelFailure.Timeout);
        }
        catch (ApiException exception)
        {
            return AiModelResult.Failed(ClassifyStatus(exception.StatusCode));
        }
        catch (HttpRequestException exception)
        {
            return AiModelResult.Failed(exception.StatusCode is { } status
                ? ClassifyStatus((int)status)
                : AiModelFailure.ProviderUnavailable);
        }
        catch (JsonException)
        {
            return AiModelResult.Failed(AiModelFailure.InvalidResponse);
        }
        catch (OptionsValidationException)
        {
            return AiModelResult.Failed(AiModelFailure.InvalidConfiguration);
        }
        catch (Exception)
        {
            // No provider message, response body, key, or original exception escapes.
            return AiModelResult.Failed(AiModelFailure.UnexpectedFailure);
        }
    }

    private static AiModelFailure ClassifyStatus(int status) => status switch
    {
        401 or 403 => AiModelFailure.AuthenticationFailed,
        429 => AiModelFailure.QuotaExceeded,
        408 or 504 => AiModelFailure.Timeout,
        404 => AiModelFailure.ModelUnavailable,
        0 or >= 500 => AiModelFailure.ProviderUnavailable,
        _ => AiModelFailure.ProviderRejected
    };
}
