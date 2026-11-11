using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Infrastructure.Persistence;

/// <summary>
/// Infrastructure boundary, not a scheduling use case. The callback must load current
/// state after the lock, validate conflicts/confirmation, increment Versao and write audit.
/// </summary>
public sealed class AgendaMutationExecutor(TelegramBusinessOperationExecutor operations)
{
    public async Task<bool> ExecuteOnceAsync(
        long updateId,
        string operationKey,
        Func<IanDbContext, CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        try
        {
            return await operations.ExecuteOnceAsync(updateId, operationKey, async (context, token) =>
            {
                await PostgresAgendaLock.AcquireAsync(context, token);
                await mutation(context, token);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AgendaVersionConflictException();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new AgendaLockUnavailableException();
        }
    }
}

public sealed class AgendaVersionConflictException()
    : Exception("O agendamento foi alterado. Releia os dados e revalide a operação.");

public sealed class AgendaLockUnavailableException()
    : Exception("A agenda está ocupada. A operação não foi confirmada.");
