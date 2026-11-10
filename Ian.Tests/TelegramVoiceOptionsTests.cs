using Ian.Application.Telegram;
using Ian.Infrastructure;
using Ian.Infrastructure.AI;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class TelegramVoiceOptionsTests
{
    [Fact]
    public void VoiceServicesResolveWithoutActivatingTheLiveDispatcher()
    {
        using var provider = CreateProvider();
        var options = provider.GetRequiredService<IOptions<TelegramVoiceOptions>>().Value;

        Assert.Equal(new TelegramVoiceLimits(), options.ToLimits());
        Assert.IsType<DeferredTelegramHandlers>(provider.GetRequiredService<ITelegramAuthorizedUpdateHandler>());
        Assert.IsType<GeminiVoiceTranscriber>(provider.GetRequiredService<IVoiceTranscriber>());
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TelegramVoiceUpdateHandler>());
    }

    [Fact]
    public void LimitsCanBeConfigured()
    {
        using var provider = CreateProvider("TelegramVoice:MaxBytes", "1024");

        Assert.Equal(1024, provider.GetRequiredService<IOptions<TelegramVoiceOptions>>().Value.MaxBytes);
    }

    [Theory]
    [InlineData("TelegramVoice:MaxBytes", "0")]
    [InlineData("TelegramVoice:MaxBytes", "20000001")]
    [InlineData("TelegramVoice:MaxBytes", "2147483648")]
    [InlineData("TelegramVoice:MaxDurationSeconds", "-1")]
    [InlineData("TelegramVoice:MaxDurationSeconds", "3601")]
    [InlineData("TelegramVoice:TimeoutSeconds", "0")]
    [InlineData("TelegramVoice:TimeoutSeconds", "301")]
    [InlineData("TelegramVoice:TimeoutSeconds", "private-invalid-value")]
    public void InvalidLimitsFailWithoutExposingValues(string key, string value)
    {
        using var provider = CreateProvider(key, value);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<TelegramVoiceOptions>>().Value);

        Assert.Contains("TelegramVoice", exception.Message);
        Assert.DoesNotContain("TEST", exception.ToString());
        Assert.DoesNotContain("private-invalid-value", exception.ToString());
    }

    private static ServiceProvider CreateProvider(string? key = null, string? value = null)
    {
        var configuration = new ConfigurationManager
        {
            ["Telegram:BotToken"] = "TEST",
            ["Telegram:AdministratorUserId"] = "42",
            ["Telegram:WebhookSecret"] = "TEST_SECRET"
        };
        if (key is not null)
        {
            configuration[key] = value;
        }

        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }
}
