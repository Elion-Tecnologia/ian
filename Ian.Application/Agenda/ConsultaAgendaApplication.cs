using Ian.Domain.Agenda;

namespace Ian.Application.Agenda;

public sealed class ConsultaAgendaApplication(IConsultaAgendaStore store) : IConsultaAgenda
{
    public const int LimiteMaximo = 50;

    private static readonly TimeZoneInfo FusoHorario =
        TimeZoneInfo.FindSystemTimeZoneById(CalculadoraOcupacao.FusoHorarioId);

    public async Task<ResultadoConsultaAgendamentos> ConsultarAsync(
        ConsultaAgendamentosFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var erro = Validar(filtro);
        if (erro is not null)
        {
            return Invalido(erro.Value);
        }

        DateTimeOffset? inicioUtc = null;
        DateTimeOffset? fimUtc = null;
        if (filtro.DataInicial is { } dataInicial)
        {
            var dataFinal = filtro.DataFinal ?? dataInicial;
            inicioUtc = ConverterInicioData(dataInicial);
            fimUtc = ConverterInicioData(dataFinal.AddDays(1));
        }

        var registros = (await store.ConsultarAsync(
            new(
                inicioUtc,
                fimUtc,
                filtro.ClienteId,
                filtro.TipoServicoId,
                filtro.Estado,
                filtro.AgendamentoId,
                filtro.Limite + 1),
            cancellationToken)).ToList();

        var maisResultados = registros.Count > filtro.Limite;
        if (maisResultados)
        {
            registros.RemoveAt(registros.Count - 1);
        }

        var itens = registros.Select(Mapear).ToArray();
        var estado = itens.Length switch
        {
            0 => EstadoConsultaAgendamentos.Nenhum,
            1 => EstadoConsultaAgendamentos.Unico,
            _ => EstadoConsultaAgendamentos.Multiplos
        };
        return new(estado, itens, maisResultados);
    }

    internal static PeriodoAgendaItem ConverterPeriodo(PeriodoAgendaStoreItem periodo)
    {
        var inicio = TimeZoneInfo.ConvertTime(periodo.InicioUtc, FusoHorario);
        var fim = TimeZoneInfo.ConvertTime(periodo.FimUtc, FusoHorario);
        return new(
            DateOnly.FromDateTime(inicio.DateTime),
            TimeOnly.FromDateTime(inicio.DateTime),
            TimeOnly.FromDateTime(fim.DateTime));
    }

    private static ErroConsultaAgenda? Validar(ConsultaAgendamentosFiltro filtro)
    {
        if (filtro.DataInicial is null && filtro.DataFinal is not null)
        {
            return ErroConsultaAgenda.DataFinalSemInicial;
        }

        if (filtro.DataInicial is { } inicio && filtro.DataFinal is { } fim && inicio > fim)
        {
            return ErroConsultaAgenda.IntervaloDatasInvalido;
        }

        if ((filtro.DataFinal ?? filtro.DataInicial) == DateOnly.MaxValue)
        {
            return ErroConsultaAgenda.DataFinalForaDoLimite;
        }

        if (filtro.ClienteId is <= 0)
        {
            return ErroConsultaAgenda.ClienteIdInvalido;
        }

        if (filtro.TipoServicoId is <= 0)
        {
            return ErroConsultaAgenda.TipoServicoIdInvalido;
        }

        if (filtro.AgendamentoId is <= 0)
        {
            return ErroConsultaAgenda.AgendamentoIdInvalido;
        }

        if (filtro.Estado is { } estado && !Enum.IsDefined(estado))
        {
            return ErroConsultaAgenda.EstadoInvalido;
        }

        return filtro.Limite is <= 0 or > LimiteMaximo
            ? ErroConsultaAgenda.LimiteInvalido
            : null;
    }

    private static DateTimeOffset ConverterInicioData(DateOnly data)
    {
        var local = data.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, FusoHorario));
    }

    private static AgendamentoConsultaItem Mapear(AgendamentoAgendaStoreItem registro) =>
        new(
            registro.Id,
            registro.ClienteId,
            registro.ClienteNome,
            registro.ClienteTelefone,
            registro.TipoServicoId,
            registro.TipoServicoNome,
            registro.Modalidade,
            registro.Duracao,
            registro.UnidadeDuracao,
            registro.Periodos.Select(ConverterPeriodo).ToArray(),
            registro.EnderecoServico,
            registro.Observacoes,
            registro.Estado,
            registro.Versao);

    private static ResultadoConsultaAgendamentos Invalido(ErroConsultaAgenda erro) =>
        new(
            EstadoConsultaAgendamentos.Invalido,
            Array.Empty<AgendamentoConsultaItem>(),
            false,
            erro);
}
