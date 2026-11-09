namespace Ian.Domain.Entities;

public sealed class Equipe
{
    public long Id { get; init; }
    public required string Nome { get; set; }
    public bool Ativa { get; set; } = true;
    public long? TelegramChatId { get; set; }
}