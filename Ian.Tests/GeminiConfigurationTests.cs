using Ian.Application.AI;
using Ian.Application.Telegram;
using Ian.Infrastructure;
using Ian.Infrastructure.AI;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class GeminiConfigurationTests
{
    [Fact]
    public async Task MissingAiConfigurationDoesNotBreakDiAndAgendaHandlerIsRegistered()
    {
        using var services = Provider();
        var model = services.GetRequiredService<IAiModel>();
        Assert.Same(model, services.GetRequiredService<IAiModel>());
        Assert.IsType<GeminiAiModel>(model);
        Assert.IsType<AgendaTelegramHandler>(services.GetRequiredService<ITelegramAuthorizedUpdateHandler>());

        var result = await model.InvokeAsync("synthetic prompt");
        Assert.Equal(AiModelFailure.InvalidConfiguration, result.Failure);
        Assert.Contains("Gemini:ApiKey", result.ErrorMessage);
    }

    [Theory]
    [InlineData("Gemini:ApiKey", "")]
    [InlineData("Gemini:ApiKey", "private\ninvalid-key")]
    [InlineData("Gemini:Model", "")]
    [InlineData("Gemini:Model", "https://other.example/model")]
    [InlineData("Gemini:TimeoutSeconds", "0")]
    [InlineData("Gemini:TimeoutSeconds", "301")]
    [InlineData("Gemini:TimeoutSeconds", "private-invalid-number")]
    [InlineData("Gemini:TimeoutSeconds", "2147483648")]
    public async Task InvalidSettingsReturnSafeFailureWithoutNetwork(string key, string value)
    {
        using var provider = Provider(key, value, validDefaults: true);
        var result = await provider.GetRequiredService<IAiModel>().InvokeAsync("synthetic prompt");
        Assert.Equal(AiModelFailure.InvalidConfiguration, result.Failure);
        Assert.Null(result.Text);
        Assert.DoesNotContain("synthetic-key", result.ErrorMessage);
        Assert.DoesNotContain("private-invalid-number", result.ErrorMessage);
        Assert.DoesNotContain("private\ninvalid-key", result.ErrorMessage);
    }

    [Fact]
    public void ConfigurationBindsWithoutExposingTheKeyInDiagnostics()
    {
        using var provider = Provider(validDefaults: true);
        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
        Assert.True(options.IsValid());
        Assert.Equal("gemini-3.1-flash-lite", options.Model);
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.DoesNotContain("synthetic-key", options.ToString());
    }

    private static ServiceProvider Provider(string? key = null, string? value = null, bool validDefaults = false)
    {
        var configuration = new ConfigurationManager
        {
            ["Telegram:BotToken"] = "synthetic-token",
            ["Telegram:AdministratorUserId"] = "42",
            ["Telegram:WebhookSecret"] = "SYNTHETIC_SECRET",
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Database=ian_t44_tests"
        };
        if (validDefaults)
        {
            configuration["Gemini:ApiKey"] = "synthetic-key";
            configuration["Gemini:Model"] = "gemini-3.1-flash-lite";
        }
        if (key is not null) configuration[key] = value;
        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }
}
