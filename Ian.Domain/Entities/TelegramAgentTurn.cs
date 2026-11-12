namespace Ian.Domain.Entities;

// Minimal recovery journal, never a transcript of model reasoning or conversation history.
public sealed class TelegramAgentTurn
{
    public long UpdateId { get; init; }
    public required long TelegramUserId { get; init; }
    public string? CommandJson { get; set; }
    public string? ResponseText { get; set; }
    public string SendState { get; set; } = "Pending";
    public int SendAttempts { get; set; }
    public long? TelegramMessageId { get; set; }
    public string? SendStatus { get; set; }
}
