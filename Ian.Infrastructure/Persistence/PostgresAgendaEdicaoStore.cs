using System.Globalization;
using Ian.Application.Agenda;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresAgendaEdicaoStore(IanDbContext context) : IAgendaEdicaoStore
{
    public Task<long?> ObterIdPorOperacaoAsync(long updateId, CancellationToken cancellationToken = default) =>
        context.TelegramBusinessOperations.AsNoTracking()
            .Where(item => item.UpdateId == updateId && item.OperationKey == EdicaoAgendamentoApplication.OperationKey)
            .Select(item => item.AgendamentoId).SingleOrDefaultAsync(cancellationToken);

    public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default) =>
        context.Agendamentos.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task SubstituirPeriodosAsync(
        Agendamento agendamento,
        IReadOnlyList<PeriodoCalculado> periodos,
        long updateId,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A substituição exige a transação da agenda.");

        await context.PeriodosOcupacao.Where(item => item.AgendamentoId == agendamento.Id)
            .ExecuteDeleteAsync(cancellationToken);
        context.PeriodosOcupacao.AddRange(periodos.Select(periodo => new PeriodoOcupacao
        {
            AgendamentoId = agendamento.Id,
            InicioUtc = periodo.InicioUtc,
            FimUtc = periodo.FimUtc
        }));
        var vinculados = await context.TelegramBusinessOperations
            .Where(item => item.UpdateId == updateId && item.OperationKey == EdicaoAgendamentoApplication.OperationKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AgendamentoId, agendamento.Id), cancellationToken);
        if (vinculados != 1)
            throw new InvalidOperationException("A edição exige uma operação idempotente.");

        context.RegistrosAuditoria.Add(new RegistroAuditoria
        {
            AgendamentoId = agendamento.Id,
            Operacao = EdicaoAgendamentoApplication.OperationKey,
            Resultado = "Editado",
            CorrelacaoId = string.Create(CultureInfo.InvariantCulture, $"update:{updateId}:versao:{agendamento.Versao}"),
            RegistradoEmUtc = DateTimeOffset.UtcNow
        });
    }
}
