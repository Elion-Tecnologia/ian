namespace Ian.Domain.Entities;

public enum EstadoEnvioTelegram
{
    Pendente,
    Enviado,
    Falhou
}

public sealed class EnvioTelegram
{
    public long Id { get; init; }
    public required long DestinoChatId { get; init; }
    public required string TipoMensagem { get; init; }
    public EstadoEnvioTelegram Estado { get; set; } = EstadoEnvioTelegram.Pendente;
    public int Tentativas { get; set; }
    public required DateTimeOffset CriadoEmUtc { get; init; }
    public DateTimeOffset? UltimaTentativaEmUtc { get; set; }
    public long? TelegramMessageId { get; set; }
}