using System.Collections.ObjectModel;

namespace Ian.Domain.Agenda;

public enum EstadoCalculoOcupacao
{
    Calculado,
    RequerEsclarecimento,
    Invalido
}

public enum MotivoCalculoOcupacao
{
    DuracaoInvalida,
    HorarioInicialObrigatorio,
    InicioForaDoExpediente,
    HorarioDiasCorridosDivergente,
    UnidadeDuracaoInvalida,
    HorarioLocalInvalidoOuAmbiguo,
    LimiteCalendarioExcedido
}

public sealed class ResultadoCalculoOcupacao
{
    private ResultadoCalculoOcupacao(
        EstadoCalculoOcupacao estado,
        IReadOnlyList<PeriodoCalculado> periodos,
        MotivoCalculoOcupacao? motivo)
    {
        Estado = estado;
        Periodos = periodos;
        Motivo = motivo;
    }

    public EstadoCalculoOcupacao Estado { get; }
    public IReadOnlyList<PeriodoCalculado> Periodos { get; }
    public MotivoCalculoOcupacao? Motivo { get; }

    internal static ResultadoCalculoOcupacao Calculado(List<PeriodoCalculado> periodos) =>
        new(
            EstadoCalculoOcupacao.Calculado,
            new ReadOnlyCollection<PeriodoCalculado>(periodos),
            null);

    internal static ResultadoCalculoOcupacao RequerEsclarecimento() =>
        new(
            EstadoCalculoOcupacao.RequerEsclarecimento,
            Array.Empty<PeriodoCalculado>(),
            MotivoCalculoOcupacao.HorarioDiasCorridosDivergente);

    internal static ResultadoCalculoOcupacao Invalido(MotivoCalculoOcupacao motivo) =>
        new(EstadoCalculoOcupacao.Invalido, Array.Empty<PeriodoCalculado>(), motivo);
}
