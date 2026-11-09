namespace Ian.Domain.Entities;

public sealed class ExcecaoExpediente
{
    public long Id { get; init; }
    public required long EquipeId { get; init; }
    public required DateOnly DataLocal { get; init; }
    public bool FolgaIntegral { get; set; }
    public TimeOnly? InicioLocal { get; set; }
    public TimeOnly? FimLocal { get; set; }
}