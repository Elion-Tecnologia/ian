namespace Ian.Domain.Entities;

public enum EstadoAgendamento
{
    Agendado,
    Concluido,
    Cancelado
}

public sealed class Agendamento
{
    public long Id { get; init; }
    public required long ClienteId { get; set; }
    public required long TipoServicoId { get; set; }

    public required ModalidadeServico Modalidade { get; set; }
    public required int Duracao { get; set; }
    public required UnidadeDuracao UnidadeDuracao { get; set; }
    public required DateTimeOffset InicioPrevistoUtc { get; set; }
    public required DateTimeOffset FimPrevistoUtc { get; set; }

    public string? EnderecoSnapshot { get; set; }
    public string? Observacoes { get; set; }
    public EstadoAgendamento Estado { get; set; } = EstadoAgendamento.Agendado;
    public long Versao { get; private set; } = 1;

    public void IncrementarVersao() => Versao = checked(Versao + 1);
}
