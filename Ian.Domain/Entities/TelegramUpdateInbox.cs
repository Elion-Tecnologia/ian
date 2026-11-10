namespace Ian.Domain.Entities;

public enum EstadoTelegramUpdate
{
    Received,
    Processing,
    Completed,
    FailedRetryable,
    FailedPaused
}

public sealed class TelegramUpdateInbox
{
    public long UpdateId { get; init; }
    public required string PayloadJson { get; init; }

    public EstadoTelegramUpdate Estado { get; set; }
        = EstadoTelegramUpdate.Received;

    public required DateTimeOffset RecebidoEmUtc { get; init; }

    public int Tentativas { get; set; }
    public DateTimeOffset? IniciadoEmUtc { get; set; }
    public DateTimeOffset? ConcluidoEmUtc { get; set; }
    public DateTimeOffset? ProximaTentativaEmUtc { get; set; }

    public Guid? LockToken { get; set; }
    public DateTimeOffset? LockAteUtc { get; set; }

    public string? UltimoErroCodigo { get; set; }
    public string? Resultado { get; set; }
}