using Ian.Api;
using Ian.Application.AI;
using Ian.Infrastructure;
using Ian.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Ian.Tests;

public sealed class GeminiSmokeTests(ITestOutputHelper output)
{
    [GeminiSmokeFact]
    [Trait("Category", "GeminiSmoke")]
    public async Task RealGeminiReturnsTextThroughProductionContract()
    {
        using var provider = Provider();
        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
        var result = await provider.GetRequiredService<IAiModel>()
            .InvokeAsync("Responda somente com a palavra OK.");

        output.WriteLine("Model={0}; timeout={1}s; result={2}; usage={3}",
            options.Model, options.TimeoutSeconds, result.Failure?.ToString() ?? "Success", result.Usage);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
    }

    [GeminiSmokeFact]
    [Trait("Category", "GeminiSmoke")]
    public async Task RealGeminiCallsOnlyTheIsolatedDemoFunction()
    {
        using var provider = Provider();
        var settings = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
        Assert.True(settings.IsValid(), "Configure Gemini:ApiKey, Gemini:Model e Gemini:TimeoutSeconds por fonte segura.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        GeminiFunctionDemo.DemoResult? result = null;
        try
        {
            result = await GeminiFunctionDemo.RunAsync(
                provider.GetRequiredService<GeminiChatClientFactory>().GetClient(), timeout.Token);
        }
        catch (Exception)
        {
            Assert.Fail("O smoke real de Function Calling falhou. Detalhes do provedor foram omitidos para preservar segredos.");
        }

        Assert.NotNull(result);
        output.WriteLine("Model={0}; demo_invocations={1}; arguments_correct={2}; final_text={3}",
            settings.Model, result.Invocations, result.ArgumentsCorrect, !string.IsNullOrWhiteSpace(result.Text));
        Assert.Equal(1, result.Invocations);
        Assert.True(result.ArgumentsCorrect);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
    }

    private static ServiceProvider Provider()
    {
        // Read only through the same configuration providers as the API, after explicit opt-in.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ApiHost).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }
}

public sealed class GeminiSmokeFactAttribute : FactAttribute
{
    public GeminiSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IAN_RUN_GEMINI_SMOKE") != "1" ||
            Environment.GetEnvironmentVariable("IAN_ALLOW_GEMINI_QUOTA") != "1")
        {
            Skip = "Smoke real exige execução e consumo de quota explicitamente autorizados; não executado por padrão.";
        }
    }
}
