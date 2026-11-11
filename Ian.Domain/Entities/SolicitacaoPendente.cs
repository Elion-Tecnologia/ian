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
    public long Id { get; set; }
    public required long TelegramUserId { get; init; }
    public required string Operacao { get; init; }
    public string DadosColetadosJson { get; set; } = "{}";
    public string CamposFaltantesJson { get; set; } = "[]";
    public string CandidatosJson { get; set; } = "{}";
    public EstadoSolicitacaoPendente Estado { get; set; } = EstadoSolicitacaoPendente.Pendente;
    public required DateTimeOffset CriadaEmUtc { get; init; }
    public DateTimeOffset AtualizadaEmUtc { get; set; }
    public required DateTimeOffset ExpiraEmUtc { get; init; }
}
