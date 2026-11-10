using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Ian.Tests;

// Deliberately lives only in tests: production exposes no tool-registration API.
internal static class GeminiFunctionDemo
{
    public static async Task<DemoResult> RunAsync(IChatClient client, CancellationToken cancellationToken)
    {
        var invocations = 0;
        var argumentsCorrect = false;
        var sum = AIFunctionFactory.Create((int left, int right) =>
        {
            invocations++;
            argumentsCorrect = left == 2 && right == 3;
            return left + right;
        }, name: "sum_demo", description: "Adds two synthetic integers without side effects.");
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            Name = "IanIsolatedFunctionDemo",
            ChatOptions = new ChatOptions { Tools = [sum], MaxOutputTokens = 2048 }
        });
        var response = await agent.RunAsync(
            "Call sum_demo once with left=2 and right=3. Then return its result as text.",
            cancellationToken: cancellationToken);
        return new DemoResult(invocations, argumentsCorrect, response.Text, response.Usage);
    }

    internal sealed record DemoResult(int Invocations, bool ArgumentsCorrect, string Text, UsageDetails? Usage);
}
