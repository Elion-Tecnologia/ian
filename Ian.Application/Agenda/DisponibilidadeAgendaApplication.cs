using Ian.Domain.Agenda;

namespace Ian.Application.Agenda;

public sealed class DisponibilidadeAgendaApplication(IConsultaAgendaStore store)
    : IDisponibilidadeAgenda
{
    public async Task<ResultadoDisponibilidadeAgenda> VerificarAsync(
        VerificarDisponibilidadeSolicitacao solicitacao,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitacao);

        var calculo = CalculadoraOcupacao.Calcular(
            solicitacao.Data,
            solicitacao.Horario,
            solicitacao.Duracao,
            solicitacao.UnidadeDuracao);
        var periodos = calculo.Periodos
            .Select(periodo => new PeriodoAgendaStoreItem(periodo.InicioUtc, periodo.FimUtc))
            .ToArray();

        if (calculo.Estado == EstadoCalculoOcupacao.RequerEsclarecimento)
        {
            return new(
                EstadoDisponibilidadeAgenda.RequerEsclarecimento,
                Array.Empty<PeriodoAgendaItem>(),
                Array.Empty<long>(),
                calculo.Motivo);
        }

        if (calculo.Estado == EstadoCalculoOcupacao.Invalido)
        {
            return new(
                EstadoDisponibilidadeAgenda.Invalido,
                Array.Empty<PeriodoAgendaItem>(),
                Array.Empty<long>(),
                calculo.Motivo);
        }

        var conflitos = await store.ObterConflitosAsync(periodos, cancellationToken);
        return new(
            conflitos.Count == 0
                ? EstadoDisponibilidadeAgenda.Disponivel
                : EstadoDisponibilidadeAgenda.Indisponivel,
            periodos.Select(ConsultaAgendaApplication.ConverterPeriodo).ToArray(),
            conflitos);
    }
}
