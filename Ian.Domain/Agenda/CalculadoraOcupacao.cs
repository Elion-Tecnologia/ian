using Ian.Domain.Entities;

namespace Ian.Domain.Agenda;

public static class CalculadoraOcupacao
{
    public const string FusoHorarioId = "America/Sao_Paulo";

    private static readonly TimeOnly InicioExpediente = new(8, 0);
    private static readonly TimeOnly FimExpediente = new(18, 0);
    private static readonly TimeSpan DuracaoExpediente = TimeSpan.FromHours(10);
    private static readonly TimeZoneInfo FusoHorario =
        TimeZoneInfo.FindSystemTimeZoneById(FusoHorarioId);

    public static ResultadoCalculoOcupacao Calcular(
        DateOnly dataInicial,
        TimeOnly? horarioInicial,
        int duracao,
        UnidadeDuracao unidadeDuracao)
    {
        if (duracao <= 0)
        {
            return ResultadoCalculoOcupacao.Invalido(MotivoCalculoOcupacao.DuracaoInvalida);
        }

        return unidadeDuracao switch
        {
            UnidadeDuracao.Minutos => CalcularMinutos(dataInicial, horarioInicial, duracao),
            UnidadeDuracao.DiasCorridos => CalcularDiasCorridos(dataInicial, horarioInicial, duracao),
            _ => ResultadoCalculoOcupacao.Invalido(MotivoCalculoOcupacao.UnidadeDuracaoInvalida)
        };
    }

    private static ResultadoCalculoOcupacao CalcularMinutos(
        DateOnly dataInicial,
        TimeOnly? horarioInicial,
        int duracao)
    {
        if (horarioInicial is null)
        {
            return ResultadoCalculoOcupacao.Invalido(
                MotivoCalculoOcupacao.HorarioInicialObrigatorio);
        }

        if (horarioInicial < InicioExpediente || horarioInicial >= FimExpediente)
        {
            return ResultadoCalculoOcupacao.Invalido(
                MotivoCalculoOcupacao.InicioForaDoExpediente);
        }

        var restante = TimeSpan.FromMinutes(duracao).Ticks;
        var disponivelPrimeiroDia = (FimExpediente - horarioInicial.Value).Ticks;
        var restanteDepoisDoPrimeiroDia = Math.Max(0, restante - disponivelPrimeiroDia);
        var diasAdicionais = restanteDepoisDoPrimeiroDia == 0
            ? 0
            : (restanteDepoisDoPrimeiroDia + DuracaoExpediente.Ticks - 1) /
                DuracaoExpediente.Ticks;

        if ((long)dataInicial.DayNumber + diasAdicionais > DateOnly.MaxValue.DayNumber)
        {
            return ResultadoCalculoOcupacao.Invalido(
                MotivoCalculoOcupacao.LimiteCalendarioExcedido);
        }

        var periodos = new List<PeriodoCalculado>();
        var data = dataInicial;
        var inicio = horarioInicial.Value;

        while (restante > 0)
        {
            var disponivel = (FimExpediente - inicio).Ticks;
            var consumido = Math.Min(restante, disponivel);
            var fim = inicio.Add(TimeSpan.FromTicks(consumido));

            if (!TryCriarPeriodo(data, inicio, fim, out var periodo) || periodo is null)
            {
                return ResultadoCalculoOcupacao.Invalido(
                    MotivoCalculoOcupacao.HorarioLocalInvalidoOuAmbiguo);
            }

            periodos.Add(periodo);
            restante -= consumido;

            if (restante > 0)
            {
                data = data.AddDays(1);
                inicio = InicioExpediente;
            }
        }

        return ResultadoCalculoOcupacao.Calculado(periodos);
    }

    private static ResultadoCalculoOcupacao CalcularDiasCorridos(
        DateOnly dataInicial,
        TimeOnly? horarioInicial,
        int duracao)
    {
        if (horarioInicial is not null && horarioInicial != InicioExpediente)
        {
            return ResultadoCalculoOcupacao.RequerEsclarecimento();
        }

        if ((long)dataInicial.DayNumber + duracao - 1 > DateOnly.MaxValue.DayNumber)
        {
            return ResultadoCalculoOcupacao.Invalido(
                MotivoCalculoOcupacao.LimiteCalendarioExcedido);
        }

        var periodos = new List<PeriodoCalculado>(duracao);

        for (var indice = 0; indice < duracao; indice++)
        {
            var data = dataInicial.AddDays(indice);
            if (!TryCriarPeriodo(data, InicioExpediente, FimExpediente, out var periodo) ||
                periodo is null)
            {
                return ResultadoCalculoOcupacao.Invalido(
                    MotivoCalculoOcupacao.HorarioLocalInvalidoOuAmbiguo);
            }

            periodos.Add(periodo);
        }

        return ResultadoCalculoOcupacao.Calculado(periodos);
    }

    private static bool TryCriarPeriodo(
        DateOnly data,
        TimeOnly inicio,
        TimeOnly fim,
        out PeriodoCalculado? periodo)
    {
        var inicioLocal = data.ToDateTime(inicio, DateTimeKind.Unspecified);
        var fimLocal = data.ToDateTime(fim, DateTimeKind.Unspecified);

        if (FusoHorario.IsInvalidTime(inicioLocal) || FusoHorario.IsAmbiguousTime(inicioLocal) ||
            FusoHorario.IsInvalidTime(fimLocal) || FusoHorario.IsAmbiguousTime(fimLocal))
        {
            periodo = null;
            return false;
        }

        var inicioUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(inicioLocal, FusoHorario));
        var fimUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(fimLocal, FusoHorario));
        periodo = new PeriodoCalculado(inicioUtc, fimUtc);
        return true;
    }
}
