namespace Ian.Domain.Entities;

public sealed class PeriodoOcupacao
{
    public long Id { get; init; }
    public required long AgendamentoId { get; init; }
    public required DateTimeOffset InicioUtc { get; set; }
    public required DateTimeOffset FimUtc { get; set; }
}