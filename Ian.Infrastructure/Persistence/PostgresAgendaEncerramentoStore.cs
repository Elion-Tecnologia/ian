using System.Globalization;
using Ian.Application.Agenda;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ian.Infrastructure.Persistence;

public sealed class PostgresAgendaEncerramentoStore(IanDbContext context) : IAgendaEncerramentoStore
{
    public Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default) =>
        context.Agendamentos.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task RegistrarAsync(
        Agendamento agendamento,
        long updateId,
        string operationKey,
        DateTimeOffset instanteUtc,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("O encerramento exige a transação da agenda.");

        var vinculados = await context.TelegramBusinessOperations
            .Where(item => item.UpdateId == updateId && item.OperationKey == operationKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AgendamentoId, agendamento.Id), cancellationToken);
        if (vinculados != 1)
            throw new InvalidOperationException("O encerramento exige uma operação idempotente.");

        context.RegistrosAuditoria.Add(new RegistroAuditoria
        {
            AgendamentoId = agendamento.Id,
            Operacao = agendamento.Estado == EstadoAgendamento.Concluido ? "agendamento:complete" : "agendamento:cancel",
            Resultado = agendamento.Estado.ToString(),
            CorrelacaoId = string.Create(CultureInfo.InvariantCulture, $"update:{updateId}:versao:{agendamento.Versao}"),
            RegistradoEmUtc = instanteUtc
        });
    }

    public Task<AgendamentoEncerradoItem?> ObterPorOperacaoAsync(
        long updateId,
        string operationKey,
        CancellationToken cancellationToken = default) =>
        (from operation in context.TelegramBusinessOperations.AsNoTracking()
         join agendamento in context.Agendamentos.AsNoTracking()
             on operation.AgendamentoId equals (long?)agendamento.Id
         where operation.UpdateId == updateId && operation.OperationKey == operationKey
         select new AgendamentoEncerradoItem(agendamento.Id, agendamento.Estado, agendamento.Versao))
        .SingleOrDefaultAsync(cancellationToken);
}
