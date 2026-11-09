namespace Ian.Domain.Entities;

public enum ModalidadeServico
{
    Interno,
    Externo,
    Remoto
}

public enum UnidadeDuracao
{
    Minutos,
    DiasCorridos
}

public sealed class TipoServico
{
    public long Id { get; init; }
    public required string Nome { get; set; }
    public required ModalidadeServico ModalidadePadrao { get; set; }
    public required int DuracaoPadrao { get; set; }
    public required UnidadeDuracao UnidadeDuracaoPadrao { get; set; }
}