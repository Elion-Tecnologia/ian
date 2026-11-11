using Ian.Application.Agenda;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Infrastructure.Persistence;

/// <summary>
/// Infrastructure boundary, not a scheduling use case. The callback must load current
/// state after the lock, validate the operation and stage all related mutations atomically.
/// </summary>
public sealed class AgendaMutationExecutor(TelegramBusinessOperationExecutor operations)
    : IAgendaMutationExecutor
{
    public Task<bool> ExecuteOnceAsync(
        long updateId,
        string operationKey,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return ExecuteOnceAsync(
            updateId,
            operationKey,
            (_, token) => mutation(token),
            cancellationToken);
    }

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
