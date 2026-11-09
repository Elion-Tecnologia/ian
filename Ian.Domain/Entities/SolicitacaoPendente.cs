namespace Ian.Domain.Entities;

public enum EstadoSolicitacaoPendente
{
    Pendente,
    Confirmada,
    Cancelada,
    Expirada
}

public sealed class SolicitacaoPendente
{
    public long Id { get; init; }
    public required long TelegramUserId { get; init; }
    public required string Operacao { get; init; }
    public EstadoSolicitacaoPendente Estado { get; set; } = EstadoSolicitacaoPendente.Pendente;
    public required DateTimeOffset CriadaEmUtc { get; init; }
    public required DateTimeOffset ExpiraEmUtc { get; init; }
}