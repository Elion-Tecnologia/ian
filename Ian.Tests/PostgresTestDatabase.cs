using System.Text.RegularExpressions;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Tests;

internal static partial class PostgresTestDatabase
{
    public static string ConnectionString => Validate(
        Environment.GetEnvironmentVariable("IAN_TEST_POSTGRES_CONNECTION_STRING"),
        Environment.GetEnvironmentVariable("IAN_TEST_POSTGRES_DATABASE"));

    public static string Validate(string? value, string? confirmedDatabase)
    {
        NpgsqlConnectionStringBuilder settings;
        try { settings = new NpgsqlConnectionStringBuilder(value); }
        catch (ArgumentException) { throw new InvalidOperationException("Configuração PostgreSQL de teste inválida."); }

        // Never accept the source, the restore validation copy, a remote host or a default app connection.
        if (settings.Host is not ("127.0.0.1" or "localhost" or "::1")
            || !TestDatabaseName().IsMatch(settings.Database ?? "")
            || settings.Database != confirmedDatabase)
            throw new InvalidOperationException(
                "Testes exigem banco local ian_t<numero>_tests confirmado em IAN_TEST_POSTGRES_DATABASE.");
        settings.IncludeErrorDetail = false;
        settings.LogParameters = false;
        settings.PersistSecurityInfo = false;
        return settings.ConnectionString;
    }

    public static IanDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IanDbContext>().UseNpgsql(ConnectionString).Options);

    public static async Task ResetAsync()
    {
        // Both environment values are validated BEFORE opening a connection or executing destructive SQL.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DROP SCHEMA IF EXISTS legado_mvp CASCADE; DROP SCHEMA public CASCADE; CREATE SCHEMA public;", connection);
        await command.ExecuteNonQueryAsync();
    }

    [GeneratedRegex(@"^ian_t[0-9]+_tests$", RegexOptions.CultureInvariant)]
    private static partial Regex TestDatabaseName();
}

public sealed class PostgresTestSafetyTests
{
    [Theory]
    [InlineData("ian_t03_tmp", "127.0.0.1", "ian_t03_tmp")]
    [InlineData("ian_t79_restore_validation", "127.0.0.1", "ian_t79_restore_validation")]
    [InlineData("ian_t79_tests", "remote.example", "ian_t79_tests")]
    [InlineData("ian_t79_tests", "127.0.0.1", null)]
    [InlineData("ian_t79_tests", "127.0.0.1", "ian_t08_tests")]
    public void RejectsUnsafeTestTargetsBeforeOpeningConnection(string database, string host, string? confirmation)
    {
        var connection = new NpgsqlConnectionStringBuilder { Host = host, Database = database, Password = "synthetic" };
        var error = Assert.Throws<InvalidOperationException>(() =>
            PostgresTestDatabase.Validate(connection.ConnectionString, confirmation));
        Assert.DoesNotContain("synthetic", error.Message);
    }
}
