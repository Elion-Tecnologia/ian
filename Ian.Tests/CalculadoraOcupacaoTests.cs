using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Tests;

public sealed class CalculadoraOcupacaoTests
{
    [Fact]
    public void MinutesWithinOneDayProduceOneUtcPeriod()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), new TimeOnly(8, 0), 240, UnidadeDuracao.Minutos);

        AssertCalculated(result,
            new PeriodoCalculado(Utc(2030, 1, 7, 11), Utc(2030, 1, 7, 15)));
    }

    [Fact]
    public void MinutesContinueAtEightOnNextDay()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), new TimeOnly(16, 0), 240, UnidadeDuracao.Minutos);

        AssertCalculated(result,
            new PeriodoCalculado(Utc(2030, 1, 7, 19), Utc(2030, 1, 7, 21)),
            new PeriodoCalculado(Utc(2030, 1, 8, 11), Utc(2030, 1, 8, 13)));
    }

    [Fact]
    public void OneCalendarDayWithoutExplicitTimeProducesFullWindow()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), null, 1, UnidadeDuracao.DiasCorridos);

        AssertCalculated(result,
            new PeriodoCalculado(Utc(2030, 1, 7, 11), Utc(2030, 1, 7, 21)));
    }

    [Fact]
    public void SevenCalendarDaysFromMondayIncludeSunday()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), new TimeOnly(8, 0), 7, UnidadeDuracao.DiasCorridos);

        Assert.Equal(EstadoCalculoOcupacao.Calculado, result.Estado);
        Assert.Equal(7, result.Periodos.Count);
        Assert.Equal(
            new PeriodoCalculado(Utc(2030, 1, 7, 11), Utc(2030, 1, 7, 21)),
            result.Periodos[0]);
        Assert.Equal(
            new PeriodoCalculado(Utc(2030, 1, 13, 11), Utc(2030, 1, 13, 21)),
            result.Periodos[6]);
        Assert.Equal(DayOfWeek.Sunday, new DateOnly(2030, 1, 13).DayOfWeek);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(18, 0)]
    [InlineData(8, 1)]
    public void DivergentTimeForCalendarDaysRequiresClarification(int hour, int minute)
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), new TimeOnly(hour, minute), 1,
            UnidadeDuracao.DiasCorridos);

        Assert.Equal(EstadoCalculoOcupacao.RequerEsclarecimento, result.Estado);
        Assert.Equal(MotivoCalculoOcupacao.HorarioDiasCorridosDivergente, result.Motivo);
        Assert.Empty(result.Periodos);
    }

    [Theory]
    [InlineData(7, 59)]
    [InlineData(18, 0)]
    [InlineData(23, 0)]
    public void MinutesOutsideBusinessHoursAreInvalid(int hour, int minute)
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), new TimeOnly(hour, minute), 60,
            UnidadeDuracao.Minutos);

        AssertInvalid(result, MotivoCalculoOcupacao.InicioForaDoExpediente);
    }

    [Fact]
    public void MinutesRequireAnInitialTime()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), null, 60, UnidadeDuracao.Minutos);

        AssertInvalid(result, MotivoCalculoOcupacao.HorarioInicialObrigatorio);
    }

    [Theory]
    [InlineData(0, UnidadeDuracao.Minutos)]
    [InlineData(-1, UnidadeDuracao.Minutos)]
    [InlineData(0, UnidadeDuracao.DiasCorridos)]
    [InlineData(-1, UnidadeDuracao.DiasCorridos)]
    public void NonPositiveDurationIsInvalid(int duration, UnidadeDuracao unit)
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), new TimeOnly(8, 0), duration, unit);

        AssertInvalid(result, MotivoCalculoOcupacao.DuracaoInvalida);
    }

    [Fact]
    public void UnknownDurationUnitIsInvalid()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 7), new TimeOnly(8, 0), 1, (UnidadeDuracao)999);

        AssertInvalid(result, MotivoCalculoOcupacao.UnidadeDuracaoInvalida);
    }

    [Fact]
    public void CalendarDaysCrossMonthBoundary()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2030, 1, 31), null, 2, UnidadeDuracao.DiasCorridos);

        AssertCalculated(result,
            new PeriodoCalculado(Utc(2030, 1, 31, 11), Utc(2030, 1, 31, 21)),
            new PeriodoCalculado(Utc(2030, 2, 1, 11), Utc(2030, 2, 1, 21)));
    }

    [Fact]
    public void MinutesCrossYearBoundary()
    {
        var result = CalculadoraOcupacao.Calcular(
            new DateOnly(2029, 12, 31), new TimeOnly(16, 0), 240, UnidadeDuracao.Minutos);

        AssertCalculated(result,
            new PeriodoCalculado(Utc(2029, 12, 31, 19), Utc(2029, 12, 31, 21)),
            new PeriodoCalculado(Utc(2030, 1, 1, 11), Utc(2030, 1, 1, 13)));
    }

    [Theory]
    [InlineData(UnidadeDuracao.Minutos, 120)]
    [InlineData(UnidadeDuracao.DiasCorridos, 2)]
    public void DurationBeyondCalendarLimitIsInvalid(UnidadeDuracao unit, int duration)
    {
        var result = CalculadoraOcupacao.Calcular(
            DateOnly.MaxValue, unit == UnidadeDuracao.Minutos ? new TimeOnly(17, 0) : null,
            duration, unit);

        AssertInvalid(result, MotivoCalculoOcupacao.LimiteCalendarioExcedido);
    }

    [Fact]
    public void OverlapUsesHalfOpenIntervalsAndAllowsAdjacency()
    {
        var first = new PeriodoCalculado(Utc(2030, 1, 7, 11), Utc(2030, 1, 7, 12));
        var overlapping = new PeriodoCalculado(Utc(2030, 1, 7, 11, 30), Utc(2030, 1, 7, 12, 30));
        var adjacent = new PeriodoCalculado(Utc(2030, 1, 7, 12), Utc(2030, 1, 7, 13));

        Assert.True(first.Sobrepoe(overlapping));
        Assert.True(overlapping.Sobrepoe(first));
        Assert.False(first.Sobrepoe(adjacent));
        Assert.False(adjacent.Sobrepoe(first));
    }

    [Fact]
    public void PeriodRejectsNonUtcOrNonPositiveRange()
    {
        Assert.Throws<ArgumentException>(() => new PeriodoCalculado(
            new DateTimeOffset(2030, 1, 7, 8, 0, 0, TimeSpan.FromHours(-3)),
            Utc(2030, 1, 7, 12)));
        Assert.Throws<ArgumentException>(() => new PeriodoCalculado(
            Utc(2030, 1, 7, 12), Utc(2030, 1, 7, 12)));
        Assert.Throws<ArgumentException>(() => new PeriodoCalculado(
            Utc(2030, 1, 7, 13), Utc(2030, 1, 7, 12)));
    }

    private static DateTimeOffset Utc(
        int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static void AssertCalculated(
        ResultadoCalculoOcupacao result,
        params PeriodoCalculado[] expected)
    {
        Assert.Equal(EstadoCalculoOcupacao.Calculado, result.Estado);
        Assert.Null(result.Motivo);
        Assert.Equal(expected, result.Periodos);
    }

    private static void AssertInvalid(
        ResultadoCalculoOcupacao result,
        MotivoCalculoOcupacao reason)
    {
        Assert.Equal(EstadoCalculoOcupacao.Invalido, result.Estado);
        Assert.Equal(reason, result.Motivo);
        Assert.Empty(result.Periodos);
    }
}
