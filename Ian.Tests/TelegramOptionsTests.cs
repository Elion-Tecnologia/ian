using Ian.Infrastructure;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public class TelegramOptionsTests
{
    [Fact]
    public void AcceptsValidSettingsWithoutPublicUrl()
    {
        var options = ResolveOptions();

        Assert.Equal("test-bot-token", options.BotToken);
        Assert.Equal(5_000_000_000L, options.AdministratorUserId);
        Assert.Equal("Test_secret-123", options.WebhookSecret);
        Assert.Null(options.PublicBaseUrl);
    }

    [Fact]
    public void AcceptsAbsoluteHttpsUrl()
    {
        var options = ResolveOptions("Telegram:PublicBaseUrl", "https://ian.example.test");

        Assert.Equal(new Uri("https://ian.example.test"), options.PublicBaseUrl);
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("_", 256)]
    public void AcceptsWebhookSecretAtLengthLimits(string character, int length)
    {
        var secret = new string(character[0], length);

        Assert.Equal(secret, ResolveOptions("Telegram:WebhookSecret", secret).WebhookSecret);
    }

    [Theory]
    [InlineData("Telegram:BotToken", "")]
    [InlineData("Telegram:BotToken", "   ")]
    [InlineData("Telegram:AdministratorUserId", "0")]
    [InlineData("Telegram:AdministratorUserId", "-1")]
    [InlineData("Telegram:AdministratorUserId", "invalid-test-admin")]
    [InlineData("Telegram:AdministratorUserId", "9223372036854775808")]
    [InlineData("Telegram:WebhookSecret", "")]
    [InlineData("Telegram:WebhookSecret", "invalid.test.secret")]
    [InlineData("Telegram:WebhookSecret", "inválido")]
    [InlineData("Telegram:PublicBaseUrl", "http://ian.example.test")]
    [InlineData("Telegram:PublicBaseUrl", "/ian-test")]
    public void RejectsInvalidSettingsWithoutDisclosingValues(string key, string value)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => ResolveOptions(key, value));

        Assert.Contains(key, exception.Message);
        Assert.DoesNotContain("test-bot-token", exception.ToString());
        Assert.DoesNotContain("Test_secret-123", exception.ToString());
        if (value.Length > 1 && !string.IsNullOrWhiteSpace(value))
        {
            Assert.DoesNotContain(value, exception.ToString());
        }
    }

    [Fact]
    public void RejectsWebhookSecretOver256Characters()
    {
        var secret = new string('A', 257);
        var exception = Assert.Throws<OptionsValidationException>(() => ResolveOptions("Telegram:WebhookSecret", secret));

        Assert.Contains("Telegram:WebhookSecret", exception.Message);
        Assert.DoesNotContain(secret, exception.ToString());
    }

    private static TelegramOptions ResolveOptions(string? key = null, string? value = null)
    {
        var configuration = new ConfigurationManager
        {
            ["Telegram:BotToken"] = "test-bot-token",
            ["Telegram:AdministratorUserId"] = "5000000000",
            ["Telegram:WebhookSecret"] = "Test_secret-123"
        };
        if (key is not null)
        {
            configuration[key] = value;
        }

        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TelegramOptions>>().Value;
    }
}
