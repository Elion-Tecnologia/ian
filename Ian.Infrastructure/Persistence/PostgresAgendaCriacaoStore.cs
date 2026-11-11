using Ian.Application.Agenda;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresAgendaCriacaoStore(IanDbContext context) : IAgendaCriacaoStore
{
    public async Task<bool> ExisteConflitoAsync(
        IReadOnlyList<PeriodoCalculado> periodos,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(periodos);
        if (periodos.Count == 0)
        {
            return false;
        }

        var inicio = periodos.Min(periodo => periodo.InicioUtc);
        var fim = periodos.Max(periodo => periodo.FimUtc);
        var ocupados = await (
            from periodo in context.PeriodosOcupacao.AsNoTracking()
            join agendamento in context.Agendamentos.AsNoTracking()
                on periodo.AgendamentoId equals agendamento.Id
            where agendamento.Estado == EstadoAgendamento.Agendado &&
                periodo.InicioUtc < fim && inicio < periodo.FimUtc
            select new { periodo.InicioUtc, periodo.FimUtc })
            .ToListAsync(cancellationToken);

        return ocupados.Any(ocupado =>
            periodos.Any(periodo =>
                periodo.InicioUtc < ocupado.FimUtc &&
                ocupado.InicioUtc < periodo.FimUtc));
    }

    public async Task<IdentificacaoAgendamento> CriarAsync(
        Agendamento agendamento,
        IReadOnlyList<PeriodoCalculado> periodos,
        long updateId,
        string operationKey,
        CancellationToken cancellationToken = default)
    {
        context.Agendamentos.Add(agendamento);
        await context.SaveChangesAsync(cancellationToken);

        context.PeriodosOcupacao.AddRange(periodos.Select(periodo => new PeriodoOcupacao
        {
            AgendamentoId = agendamento.Id,
            InicioUtc = periodo.InicioUtc,
            FimUtc = periodo.FimUtc
        }));

        var atualizados = await context.TelegramBusinessOperations
            .Where(operation =>
                operation.UpdateId == updateId &&
                operation.OperationKey == operationKey)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    operation => operation.AgendamentoId,
                    agendamento.Id),
                cancellationToken);
        if (atualizados != 1)
        {
            throw new InvalidOperationException(
                "A operação idempotente não foi vinculada ao agendamento.");
        }

        return new(agendamento.Id, agendamento.ClienteId);
    }

    public async Task<IdentificacaoAgendamento?> ObterPorOperacaoAsync(
        long updateId,
        string operationKey,
        CancellationToken cancellationToken = default) =>
        await (
            from operation in context.TelegramBusinessOperations.AsNoTracking()
            join agendamento in context.Agendamentos.AsNoTracking()
                on operation.AgendamentoId equals (long?)agendamento.Id
            where operation.UpdateId == updateId &&
                operation.OperationKey == operationKey
            select new IdentificacaoAgendamento(agendamento.Id, agendamento.ClienteId))
            .SingleOrDefaultAsync(cancellationToken);
}
