using Ian.Application.AI;

namespace Ian.Infrastructure.AI;

/// <summary>
/// Business operations are deliberately connected by the authorized orchestration in #44.
/// Until then, exposed contracts fail closed instead of pretending an operation succeeded.
/// </summary>
public sealed class AgentToolBackend : IAgentToolBackend
{
    public Task<AgentToolResult> ExecuteAsync(AgentToolRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AgentToolResult.Failed("Esta operação ainda não está disponível."));
    }
}
