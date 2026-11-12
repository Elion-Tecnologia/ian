using Ian.Application.Agenda;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresConsultaAgendaStore(IanDbContext context) : IConsultaAgendaStore
{
    public async Task<IReadOnlyList<AgendamentoAgendaStoreItem>> ConsultarAsync(
        ConsultaAgendaStoreFiltro filtro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var query = context.Agendamentos.AsNoTracking().AsQueryable();
        if (filtro.ClienteId is { } clienteId)
        {
            query = query.Where(agendamento => agendamento.ClienteId == clienteId);
        }

        if (filtro.TipoServicoId is { } tipoServicoId)
        {
            query = query.Where(agendamento => agendamento.TipoServicoId == tipoServicoId);
        }

        if (filtro.Estado is { } estado)
        {
            query = query.Where(agendamento => agendamento.Estado == estado);
        }

        if (filtro.AgendamentoId is { } agendamentoId)
        {
            query = query.Where(agendamento => agendamento.Id == agendamentoId);
        }

        List<long> idsOrdenados;
        if (filtro.InicioUtc is { } inicioUtc && filtro.FimUtc is { } fimUtc)
        {
            idsOrdenados = await (
                from agendamento in query
                join periodo in context.PeriodosOcupacao.AsNoTracking()
                    on agendamento.Id equals periodo.AgendamentoId
                where periodo.InicioUtc < fimUtc && inicioUtc < periodo.FimUtc
                group periodo by agendamento.Id
                into periodosAgendamento
                orderby periodosAgendamento.Min(periodo => periodo.InicioUtc),
                    periodosAgendamento.Key
                select periodosAgendamento.Key)
                .Take(filtro.Limite)
                .ToListAsync(cancellationToken);
        }
        else
        {
            idsOrdenados = await (
                from agendamento in query
                join periodo in context.PeriodosOcupacao.AsNoTracking()
                    on agendamento.Id equals periodo.AgendamentoId
                group periodo by agendamento.Id
                into periodosAgendamento
                orderby periodosAgendamento.Min(periodo => periodo.InicioUtc),
                    periodosAgendamento.Key
                select periodosAgendamento.Key)
                .Take(filtro.Limite)
                .ToListAsync(cancellationToken);
        }
        if (idsOrdenados.Count == 0)
        {
            return Array.Empty<AgendamentoAgendaStoreItem>();
        }

        var dados = await (
            from agendamento in context.Agendamentos.AsNoTracking()
            join cliente in context.Clientes.AsNoTracking()
                on agendamento.ClienteId equals cliente.Id
            join tipo in context.TiposServico.AsNoTracking()
                on agendamento.TipoServicoId equals tipo.Id
            where idsOrdenados.Contains(agendamento.Id)
            select new AgendamentoDados(
                agendamento.Id,
                cliente.Id,
                cliente.Nome,
                cliente.TelefoneNormalizado,
                tipo.Id,
                tipo.Nome,
                agendamento.Modalidade,
                agendamento.Duracao,
                agendamento.UnidadeDuracao,
                agendamento.EnderecoSnapshot,
                agendamento.Observacoes,
                agendamento.Estado,
                agendamento.Versao))
            .ToListAsync(cancellationToken);

        var periodosQuery = context.PeriodosOcupacao
            .AsNoTracking()
            .Where(periodo => idsOrdenados.Contains(periodo.AgendamentoId));
        if (filtro.InicioUtc is { } periodoInicio && filtro.FimUtc is { } periodoFim)
        {
            periodosQuery = periodosQuery.Where(periodo =>
                periodo.InicioUtc < periodoFim && periodoInicio < periodo.FimUtc);
        }

        var periodos = await periodosQuery
            .OrderBy(periodo => periodo.InicioUtc)
            .ThenBy(periodo => periodo.Id)
            .Select(periodo => new PeriodoComAgendamento(
                periodo.AgendamentoId,
                periodo.InicioUtc,
                periodo.FimUtc))
            .ToListAsync(cancellationToken);
        var periodosPorAgendamento = periodos.ToLookup(periodo => periodo.AgendamentoId);
        var dadosPorId = dados.ToDictionary(item => item.Id);

        return idsOrdenados.Select(id =>
        {
            var item = dadosPorId[id];
            return new AgendamentoAgendaStoreItem(
                item.Id,
                item.ClienteId,
                item.ClienteNome,
                item.ClienteTelefone,
                item.TipoServicoId,
                item.TipoServicoNome,
                item.Modalidade,
                item.Duracao,
                item.UnidadeDuracao,
                periodosPorAgendamento[id]
                    .Select(periodo => new PeriodoAgendaStoreItem(
                        periodo.InicioUtc,
                        periodo.FimUtc))
                    .ToArray(),
                item.EnderecoServico,
                item.Observacoes,
                item.Estado,
                item.Versao);
        }).ToArray();
    }

    public async Task<IReadOnlyList<long>> ObterConflitosAsync(
        IReadOnlyList<PeriodoAgendaStoreItem> periodos,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(periodos);
        if (periodos.Count == 0)
        {
            return Array.Empty<long>();
        }

        var inicio = periodos.Min(periodo => periodo.InicioUtc);
        var fim = periodos.Max(periodo => periodo.FimUtc);
        var candidatos = await (
            from periodo in context.PeriodosOcupacao.AsNoTracking()
            join agendamento in context.Agendamentos.AsNoTracking()
                on periodo.AgendamentoId equals agendamento.Id
            where agendamento.Estado == EstadoAgendamento.Agendado &&
                periodo.InicioUtc < fim && inicio < periodo.FimUtc
            select new PeriodoCandidato(
                agendamento.Id,
                periodo.InicioUtc,
                periodo.FimUtc))
            .ToListAsync(cancellationToken);

        return candidatos
            .Where(candidato => periodos.Any(periodo =>
                periodo.InicioUtc < candidato.FimUtc &&
                candidato.InicioUtc < periodo.FimUtc))
            .Select(candidato => candidato.AgendamentoId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    private sealed record AgendamentoDados(
        long Id,
        long ClienteId,
        string ClienteNome,
        string ClienteTelefone,
        long TipoServicoId,
        string TipoServicoNome,
        ModalidadeServico Modalidade,
        int Duracao,
        UnidadeDuracao UnidadeDuracao,
        string? EnderecoServico,
        string? Observacoes,
        EstadoAgendamento Estado,
        long Versao);

    private sealed record PeriodoComAgendamento(
        long AgendamentoId,
        DateTimeOffset InicioUtc,
        DateTimeOffset FimUtc);

    private sealed record PeriodoCandidato(
        long AgendamentoId,
        DateTimeOffset InicioUtc,
        DateTimeOffset FimUtc);
}
