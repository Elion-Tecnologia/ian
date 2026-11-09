namespace Ian.Infrastructure.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public long AdministratorUserId { get; set; }
    public string WebhookSecret { get; set; } = string.Empty;
    public Uri? PublicBaseUrl { get; set; }
}
