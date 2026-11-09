namespace Ian.Domain.Entities;

public enum EstadoTelegramUpdate
{
    Received
}

public sealed class TelegramUpdateInbox
{
    public long UpdateId { get; init; }
    public required string PayloadJson { get; init; }

    public EstadoTelegramUpdate Estado { get; set; }
        = EstadoTelegramUpdate.Received;

    public required DateTimeOffset RecebidoEmUtc { get; init; }
}