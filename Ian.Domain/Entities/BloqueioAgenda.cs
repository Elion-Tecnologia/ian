namespace Ian.Domain.Entities;

public sealed class BloqueioAgenda
{
    public long Id { get; init; }
    public long? EquipeId { get; set; }
    public required DateTimeOffset InicioUtc { get; set; }
    public required DateTimeOffset FimUtc { get; set; }
}