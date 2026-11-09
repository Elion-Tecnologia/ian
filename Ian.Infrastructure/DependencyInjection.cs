using System.Globalization;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        return services;
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
