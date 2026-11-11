namespace Ian.Domain.Agenda;

public sealed record PeriodoCalculado
{
    public PeriodoCalculado(DateTimeOffset inicioUtc, DateTimeOffset fimUtc)
    {
        if (inicioUtc.Offset != TimeSpan.Zero || fimUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Os períodos calculados devem usar UTC.");
        }

        if (inicioUtc >= fimUtc)
        {
            throw new ArgumentException("O início do período deve ser anterior ao fim.");
        }

        InicioUtc = inicioUtc;
        FimUtc = fimUtc;
    }

    public DateTimeOffset InicioUtc { get; }
    public DateTimeOffset FimUtc { get; }

    public bool Sobrepoe(PeriodoCalculado outro)
    {
        ArgumentNullException.ThrowIfNull(outro);
        return InicioUtc < outro.FimUtc && outro.InicioUtc < FimUtc;
    }
}
