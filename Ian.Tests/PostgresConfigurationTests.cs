using Ian.Infrastructure;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ian.Tests;

public class PostgresConfigurationTests
{
    [Fact]
    public void RegistersPostgresWithSensitiveDriverOptionsDisabled()
    {
        using var provider = BuildProvider(
            "Host=localhost;Database=ian_test;Username=ian;Password=senha-ficticia;" +
            "Include Error Detail=true;Log Parameters=true;Persist Security Info=true");
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<IanDbContext>();
        var settings = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());

        Assert.True(context.Database.IsNpgsql());
        Assert.Equal("ian_test", settings.Database);
        Assert.False(settings.IncludeErrorDetail);
        Assert.False(settings.LogParameters);
        Assert.False(settings.PersistSecurityInfo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Database=ian_test;Password=senha-ficticia")]
    [InlineData("Host=localhost;Password=senha-ficticia;Port=senha-ficticia")]
    public void RejectsInvalidConfigurationWithoutExposingPassword(string? value)
    {
        using var provider = BuildProvider(value);
        using var scope = provider.CreateScope();

        var error = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IanDbContext>());

        Assert.Contains("ConnectionStrings:Default", error.Message);
        Assert.DoesNotContain("senha-ficticia", error.ToString());
        Assert.Null(error.InnerException);
    }

    private static ServiceProvider BuildProvider(string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = value
            })
            .Build();

        return new ServiceCollection()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }
}