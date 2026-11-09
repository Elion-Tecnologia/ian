using Ian.Api;
using Ian.Infrastructure.Telegram;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public class ApiStartupTests
{
    [Fact]
    public async Task StartsInDevelopmentAndServesTheHealthEndpoint()
    {
        await using var app = BuildApp();
        await app.StartAsync();

        var options = app.Services.GetRequiredService<IOptions<TelegramOptions>>().Value;
        Assert.Equal(42, options.AdministratorUserId);

        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(new Uri(app.Urls.Single()), "/app/v1/health"));
        response.EnsureSuccessStatusCode();
        Assert.Equal("{\"status\":\"Healthy\"}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Telegram:BotToken", "")]
    [InlineData("Telegram:AdministratorUserId", "0")]
    [InlineData("Telegram:WebhookSecret", "invalid.test.secret")]
    [InlineData("Telegram:PublicBaseUrl", "http://ian.example.test")]
    public async Task RejectsInvalidConfigurationAtStartup(string key, string value)
    {
        await using var app = BuildApp(key, value);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());

        Assert.Contains(key, exception.Message);
    }

    private static WebApplication BuildApp(string? key = null, string? value = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ApiHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Development
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Configuration["Telegram:BotToken"] = "test-bot-token";
        builder.Configuration["Telegram:AdministratorUserId"] = "42";
        builder.Configuration["Telegram:WebhookSecret"] = "Test_secret-123";

        if (key is not null)
        {
            builder.Configuration[key] = value;
        }

        var app = ApiHost.Build(builder);
        app.Urls.Add("http://127.0.0.1:0");
        return app;
    }
}
