using System.Globalization;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ian.Application.Telegram;

namespace Ian.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TelegramOptions.SectionName);

        services.AddOptions<TelegramOptions>()
            .Configure(options => ReadTelegramOptions(options, section))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BotToken),
                "Telegram:BotToken é obrigatório.")
            .Validate(options => options.AdministratorUserId > 0,
                "Telegram:AdministratorUserId deve ser um inteiro positivo de 64 bits.")
            .Validate(options => IsValidWebhookSecret(options.WebhookSecret),
                "Telegram:WebhookSecret deve conter de 1 a 256 caracteres entre A-Z, a-z, 0-9, _ e -.")
            .ValidateOnStart();

        services.AddOptions<TelegramVoiceOptions>()
            .Configure(options => ReadTelegramVoiceOptions(options, configuration))
            .Validate(options => options.ToLimits().IsValid(),
                "TelegramVoice: limites de tamanho, duração ou timeout inválidos.")
            .ValidateOnStart();

        services.AddSingleton(provider => new TelegramVoiceFileClient(
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            provider.GetRequiredService<IOptions<TelegramOptions>>(),
            provider.GetRequiredService<IOptions<TelegramVoiceOptions>>()));
        services.AddSingleton<ITelegramVoiceFileClient>(provider =>
            provider.GetRequiredService<TelegramVoiceFileClient>());
        services.AddSingleton<ITelegramVoiceFeedback>(provider =>
            provider.GetRequiredService<TelegramVoiceFileClient>());

        services.AddDbContext<IanDbContext>(options =>
        {
            options.UseNpgsql(ReadPostgresConnectionString(configuration));
            options.EnableSensitiveDataLogging(false);
        });

        services.AddScoped<ITelegramUpdateInboxWriter,TelegramUpdateInboxWriter>();

        services.AddOptions<TelegramInboxProcessingOptions>()
            .Validate(
                options => options.IsValid(),
                "As opções de processamento da inbox Telegram são inválidas.")
            .ValidateOnStart();

        services.AddSingleton(_ =>
            NpgsqlDataSource.Create(
                ReadPostgresConnectionString(configuration)));

        services.AddScoped<
            ITelegramInboxProcessingStore,
            PostgresTelegramInboxProcessingStore>();

        services.AddScoped<TelegramBusinessOperationExecutor>();

        services.AddScoped<DeferredTelegramHandlers>();

        services.AddScoped<ITelegramAuthorizedUpdateHandler>(
            provider =>
                provider.GetRequiredService<DeferredTelegramHandlers>());

        services.AddScoped<ITelegramGroupEventHandler>(
            provider =>
                provider.GetRequiredService<DeferredTelegramHandlers>());

        services.AddScoped<TelegramUpdateDispatcher>();
        services.AddScoped<TelegramInboxConsumer>();

        services.AddHostedService<TelegramInboxBackgroundService>();

        return services;
    }
    private static void ReadTelegramVoiceOptions(
        TelegramVoiceOptions options,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(TelegramVoiceOptions.SectionName);
        options.MaxBytes = ReadInteger(nameof(options.MaxBytes), options.MaxBytes);
        options.MaxDurationSeconds = ReadInteger(nameof(options.MaxDurationSeconds), options.MaxDurationSeconds);
        options.TimeoutSeconds = ReadInteger(nameof(options.TimeoutSeconds), options.TimeoutSeconds);

        int ReadInteger(string name, int defaultValue)
        {
            var value = section[name];
            return value is null ? defaultValue :
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed : 0;
        }
    }

    private static string ReadPostgresConnectionString(IConfiguration configuration)
    {
        const string key = "ConnectionStrings:Default";
        var value = configuration.GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} deve ser configurada para usar o PostgreSQL.");
        }

        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(value);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"{key} tem formato inválido para o PostgreSQL.");
        }

        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            throw new InvalidOperationException($"{key} deve informar o Host do PostgreSQL.");
        }

        connection.IncludeErrorDetail = false;
        connection.LogParameters = false;
        connection.PersistSecurityInfo = false;

        return connection.ConnectionString;
    }

    private static void ReadTelegramOptions(TelegramOptions options, IConfiguration section)
    {
        options.BotToken = section[nameof(TelegramOptions.BotToken)] ?? string.Empty;
        options.WebhookSecret = section[nameof(TelegramOptions.WebhookSecret)] ?? string.Empty;

        // Parse explicitly so conversion errors cannot include configuration values.
        long.TryParse(section[nameof(TelegramOptions.AdministratorUserId)],
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var administratorUserId);
        options.AdministratorUserId = administratorUserId;

        var publicBaseUrl = section[nameof(TelegramOptions.PublicBaseUrl)];
        if (string.IsNullOrEmpty(publicBaseUrl))
        {
            return;
        }

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(TelegramOptions),
                ["Telegram:PublicBaseUrl deve ser uma URL HTTPS absoluta."]);
        }

        options.PublicBaseUrl = url;
    }

    private static bool IsValidWebhookSecret(string secret) =>
        secret.Length is >= 1 and <= 256 &&
        secret.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
