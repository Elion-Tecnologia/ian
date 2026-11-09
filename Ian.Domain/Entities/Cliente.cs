namespace Ian.Domain.Entities;

public sealed class Cliente
{
    public long Id { get; init; }
    public required string Nome { get; set; }
    public required string TelefoneNormalizado { get; set; }
    public string? EnderecoPrincipal { get; set; }
}