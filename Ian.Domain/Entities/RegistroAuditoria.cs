namespace Ian.Domain.Entities;

public sealed class RegistroAuditoria
{
    public long Id { get; init; }
    public long? AgendamentoId { get; init; }
    public required string Operacao { get; init; }
    public required string Resultado { get; init; }
    public required string CorrelacaoId { get; init; }
    public required DateTimeOffset RegistradoEmUtc { get; init; }
}