using Google.GenAI;
using Ian.Application.AI;
using Ian.Infrastructure.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class GeminiAiModelTests
{
    [Fact]
    public async Task MafReturnsTextAndUsageWithoutInstallingTools()
    {
        var fake = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, " synthetic answer "))
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 4, OutputTokenCount = 2, TotalTokenCount = 6 }
            }
        };

        var result = await Model(fake).InvokeAsync("synthetic prompt");

        Assert.True(result.IsSuccess);
        Assert.Equal("synthetic answer", result.Text);
        Assert.Equal(new AiModelUsage(4, 2, 6), result.Usage);
        Assert.Equal("Success", result.ToString());
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(fake.Options);
        Assert.Equal(ChatToolMode.None, fake.Options.ToolMode);
        Assert.True(fake.Options.Tools is null || fake.Options.Tools.Count == 0);
        Assert.Equal("gemini-3.1-flash-lite", fake.Options.ModelId);
        Assert.Equal("synthetic prompt", Assert.Single(fake.Messages).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid\0prompt")]
    public async Task InvalidPromptNeverCallsTheClient(string? prompt)
    {
        var fake = new FakeChatClient();

        var result = await Model(fake).InvokeAsync(prompt!);

        Assert.Equal(AiModelFailure.InvalidPrompt, result.Failure);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task OversizedPromptNeverCallsTheClient()
    {
        var fake = new FakeChatClient();
        var result = await Model(fake).InvokeAsync(new string('x', 16_001));
        Assert.Equal(AiModelFailure.InvalidPrompt, result.Failure);
        Assert.Equal(0, fake.Calls);
    }

    [Theory]
    [InlineData(401, AiModelFailure.AuthenticationFailed)]
    [InlineData(403, AiModelFailure.AuthenticationFailed)]
    [InlineData(429, AiModelFailure.QuotaExceeded)]
    [InlineData(408, AiModelFailure.Timeout)]
    [InlineData(504, AiModelFailure.Timeout)]
    [InlineData(503, AiModelFailure.ProviderUnavailable)]
    [InlineData(0, AiModelFailure.ProviderUnavailable)]
    [InlineData(404, AiModelFailure.ModelUnavailable)]
    [InlineData(400, AiModelFailure.ProviderRejected)]
    public async Task ProviderErrorsAreClassifiedWithoutLeakingBodies(int status, AiModelFailure expected)
    {
        var fake = new FakeChatClient { Failure = new ApiException("PRIVATE_PROVIDER_BODY", status, "ERROR", null) };

        var result = await Model(fake).InvokeAsync("synthetic prompt");

        Assert.Equal(expected, result.Failure);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Text);
        Assert.Null(result.Usage);
        Assert.DoesNotContain("PRIVATE_PROVIDER_BODY", result.ToString());
        Assert.DoesNotContain("PRIVATE_PROVIDER_BODY", result.ErrorMessage);
    }

    [Fact]
    public async Task NetworkFailureIsTransientAndSanitized()
    {
        var result = await Model(new FakeChatClient
        {
            Failure = new HttpRequestException("PRIVATE_NETWORK_MESSAGE")
        }).InvokeAsync("synthetic prompt");

        Assert.Equal(AiModelFailure.ProviderUnavailable, result.Failure);
        Assert.DoesNotContain("PRIVATE_NETWORK_MESSAGE", result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyResponseIsNotSuccess(string text)
    {
        var fake = new FakeChatClient { Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) };

        var result = await Model(fake).InvokeAsync("synthetic prompt");

        Assert.Equal(AiModelFailure.InvalidResponse, result.Failure);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task TruncatedResponseIsNotSuccess()
    {
        var fake = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "incomplete"))
            {
                FinishReason = ChatFinishReason.Length
            }
        };
        var result = await Model(fake).InvokeAsync("synthetic prompt");
        Assert.Equal(AiModelFailure.InvalidResponse, result.Failure);
    }

    [Fact]
    public async Task SeparateReasoningIsNotExposedAsTheAnswer()
    {
        var fake = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new TextReasoningContent("synthetic private reasoning"), new TextContent("synthetic final answer")]))
        };

        var result = await Model(fake).InvokeAsync("synthetic prompt");

        Assert.Equal("synthetic final answer", result.Text);
        Assert.DoesNotContain("reasoning", result.Text);
    }

    [Fact]
    public async Task UnexpectedFailureIsSanitized()
    {
        var result = await Model(new FakeChatClient
        {
            Failure = new InvalidOperationException("PRIVATE_UNEXPECTED_MESSAGE")
        }).InvokeAsync("synthetic prompt");

        Assert.Equal(AiModelFailure.UnexpectedFailure, result.Failure);
        Assert.DoesNotContain("PRIVATE_UNEXPECTED_MESSAGE", result.ErrorMessage);
    }

    [Fact]
    public async Task UnrequestedFunctionCallIsRejectedWithoutAnInvocationLoop()
    {
        var fake = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("synthetic-call", "unregistered_operation")]))
        };

        var result = await Model(fake).InvokeAsync("synthetic prompt");

        Assert.Equal(AiModelFailure.InvalidResponse, result.Failure);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotReachClient()
    {
        var fake = new FakeChatClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Model(fake).InvokeAsync("synthetic prompt", cancellation.Token));

        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task TimeoutStopsTheClientAndReturnsTypedFailure()
    {
        var fake = new FakeChatClient { WaitForCancellation = true };
        var result = await Model(fake, timeoutSeconds: 1).InvokeAsync("synthetic prompt");
        Assert.Equal(AiModelFailure.Timeout, result.Failure);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutInventingAResponse()
    {
        var fake = new FakeChatClient { WaitForCancellation = true };
        using var cancellation = new CancellationTokenSource();
        var pending = Model(fake).InvokeAsync("synthetic prompt", cancellation.Token);
        await fake.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task CallsDoNotShareConversationHistory()
    {
        var fake = new FakeChatClient();
        var model = Model(fake);
        await model.InvokeAsync("first synthetic request");
        await model.InvokeAsync("second synthetic request");
        Assert.Equal("second synthetic request", Assert.Single(fake.Messages).Text);
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public void DomainAndApplicationDoNotReferenceProviderOrFramework()
    {
        foreach (var assembly in new[] { typeof(IAiModel).Assembly, typeof(Ian.Domain.Entities.Cliente).Assembly })
        {
            var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
            Assert.DoesNotContain(references, name => name is not null &&
                (name.StartsWith("Google.", StringComparison.Ordinal) ||
                 name.StartsWith("Microsoft.Agents.", StringComparison.Ordinal) ||
                 name.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal) ||
                 name is "Ian.Infrastructure" or "Ian.Api"));
        }
    }

    private static GeminiAiModel Model(FakeChatClient fake, int timeoutSeconds = 60) =>
        new(Options.Create(new GeminiOptions
        {
            ApiKey = "synthetic-key",
            Model = "gemini-3.1-flash-lite",
            TimeoutSeconds = timeoutSeconds
        }), () => fake);

    private sealed class FakeChatClient : IChatClient
    {
        public ChatResponse Response { get; init; } = new(new ChatMessage(ChatRole.Assistant, "synthetic answer"));
        public Exception? Failure { get; init; }
        public bool WaitForCancellation { get; init; }
        public int Calls { get; private set; }
        public ChatOptions? Options { get; private set; }
        public List<ChatMessage> Messages { get; private set; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Messages = messages.ToList();
            Options = options;
            Started.TrySetResult();
            if (Failure is not null) throw Failure;
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
