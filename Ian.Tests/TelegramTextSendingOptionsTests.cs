using Ian.Application.Telegram;
using Ian.Infrastructure;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class TelegramTextSendingOptionsTests
{
    [Fact]
    public void DefaultSendingLimitsAndSingletonClientResolveWithoutDatabaseOrNetworkAccess()
    {
        using var provider = Provider();
        var options = provider.GetRequiredService<IOptions<TelegramTextSendingOptions>>().Value;
        Assert.Equal(10, options.RequestTimeoutSeconds);
        Assert.Equal(30, options.TotalTimeoutSeconds);
        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(10, options.MaxRetryDelaySeconds);
        var sender = provider.GetRequiredService<ITelegramTextSender>();
        Assert.IsType<TelegramTextClient>(sender);
        Assert.Same(sender, provider.GetRequiredService<ITelegramTextSender>());
    }

    [Theory]
    [InlineData("RequestTimeoutSeconds", "0")]
    [InlineData("RequestTimeoutSeconds", "31")]
    [InlineData("RequestTimeoutSeconds", "PRIVATE_INVALID_SETTING")]
    [InlineData("TotalTimeoutSeconds", "9")]
    [InlineData("TotalTimeoutSeconds", "61")]
    [InlineData("MaxAttempts", "0")]
    [InlineData("MaxAttempts", "4")]
    [InlineData("MaxRetryDelaySeconds", "0")]
    [InlineData("MaxRetryDelaySeconds", "31")]
    public void UnsafeSendingLimitsAreRejectedWithoutDisclosingConfigurationValues(string key, string value)
    {
        using var provider = Provider(key, value);
        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<TelegramTextSendingOptions>>().Value);
        Assert.Contains("Telegram:Sending", exception.Message);
        Assert.DoesNotContain("PRIVATE_INVALID_SETTING", exception.ToString());
        Assert.DoesNotContain("synthetic-token", exception.ToString());
    }

    [Fact]
    public void ExplicitSendingLimitsAreReadFromTheirOwnConfigurationSection()
    {
        using var provider = Provider("MaxAttempts", "1");
        Assert.Equal(1, provider.GetRequiredService<IOptions<TelegramTextSendingOptions>>().Value.MaxAttempts);
    }

    private static ServiceProvider Provider(string? key = null, string? value = null)
    {
        var configuration = new ConfigurationManager
        {
            ["Telegram:BotToken"] = "synthetic-token",
            ["Telegram:AdministratorUserId"] = "42",
            ["Telegram:WebhookSecret"] = "synthetic_secret"
        };
        if (key is not null) configuration[$"Telegram:Sending:{key}"] = value;
        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }
}
