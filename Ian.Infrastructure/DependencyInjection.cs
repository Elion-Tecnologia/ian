using System.Globalization;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ian.Application.Telegram;
using Ian.Application.AI;
using Ian.Application.Catalogo;
using Ian.Application.Clientes;
using Ian.Application.Agenda;
using Ian.Application.Conversation;
using Ian.Infrastructure.AI;

namespace Ian.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // AI remains inactive until invoked. Missing AI credentials must not stop Telegram/health.
        services.AddOptions<GeminiOptions>().Configure(options =>
        {
            var gemini = configuration.GetSection(GeminiOptions.SectionName);
            options.ApiKey = gemini[nameof(GeminiOptions.ApiKey)] ?? string.Empty;
            options.Model = gemini[nameof(GeminiOptions.Model)] ?? string.Empty;
            if (gemini[nameof(GeminiOptions.TimeoutSeconds)] is { } timeout)
            {
                int.TryParse(timeout, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds);
                options.TimeoutSeconds = seconds;
            }
        });
        services.AddSingleton(provider => new GeminiChatClientFactory(
            provider.GetRequiredService<IOptions<GeminiOptions>>(),
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                Timeout = Timeout.InfiniteTimeSpan
            }));
        services.AddSingleton<IAiModel>(provider => new GeminiAiModel(
            provider.GetRequiredService<IOptions<GeminiOptions>>(),
            provider.GetRequiredService<GeminiChatClientFactory>().GetClient));
        services.AddScoped<IAgentToolBackend, AgentToolBackend>();
        services.AddScoped<AgentExecutionBudget>();
        services.AddScoped<IAgentToolRegistry, AgentToolRegistry>();

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

        services.AddDbContext<IanDbContext>(options =>
        {
            options.UseNpgsql(ReadPostgresConnectionString(configuration));
            options.EnableSensitiveDataLogging(false);
        });

        services.AddScoped<ITelegramUpdateInboxWriter,TelegramUpdateInboxWriter>();
        services.AddScoped<IPendingConversationRepository, PostgresPendingConversationRepository>();
        services.AddScoped<IPendingConversation, PendingConversationService>();

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
        services.AddScoped<AgendaMutationExecutor>();
        services.AddScoped<IAgendaMutationExecutor>(provider =>
            provider.GetRequiredService<AgendaMutationExecutor>());
        services.AddScoped<IAgendaCriacaoStore, PostgresAgendaCriacaoStore>();
        services.AddScoped<ICriacaoAgendamento, CriacaoAgendamentoApplication>();
        services.AddScoped<IConsultaAgendaStore, PostgresConsultaAgendaStore>();
        services.AddScoped<IConsultaAgenda, ConsultaAgendaApplication>();
        services.AddScoped<IDisponibilidadeAgenda, DisponibilidadeAgendaApplication>();
        services.AddScoped<ITipoServicoCatalogo, PostgresTipoServicoCatalogo>();
        services.AddScoped<IClienteStore, PostgresClienteStore>();
        services.AddScoped<IClientes, ClientesApplication>();

        services.AddScoped<DeferredTelegramHandlers>();

        services.AddScoped<ITelegramAuthorizedUpdateHandler>(
            provider =>
                provider.GetRequiredService<DeferredTelegramHandlers>());

        services.AddScoped<TelegramUpdateDispatcher>();
        services.AddScoped<TelegramInboxConsumer>();

        services.AddHostedService<TelegramInboxBackgroundService>();

        return services;
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
