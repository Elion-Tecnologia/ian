namespace Ian.Domain.Entities;

public sealed class TelegramBusinessOperation
{
    public long UpdateId { get; init; }
    public required string OperationKey { get; init; }
    public DateTimeOffset ExecutadoEmUtc { get; init; }
    public long? AgendamentoId { get; set; }
}
