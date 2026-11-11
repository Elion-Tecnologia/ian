using System.Data;
using System.Diagnostics;
using Ian.Application.Agenda;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ian.Tests;

// These callbacks are synthetic probes of persistence, not production scheduling use cases.
[Collection("Telegram Inbox PostgreSQL")]
public sealed class AgendaConcurrencyPostgresTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 7, 11, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task GlobalLockSerializesDifferentUpdatesAndSecondConnectionSeesCommittedConflict()
    {
        await Initialize();
        await using var first = PostgresTestDatabase.CreateContext();
        await using var second = PostgresTestDatabase.CreateContext();
        await second.Database.OpenConnectionAsync();
        var secondPid = ((NpgsqlConnection)second.Database.GetDbConnection()).ProcessID;
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var one = Executor(first).ExecuteOnceAsync(1, "agenda:probe", async (context, token) =>
        {
            Assert.False(await Overlaps(context, Start, Start.AddHours(1), token));
            var appointment = Reservation();
            context.Agendamentos.Add(appointment);
            await context.SaveChangesAsync(token);
            context.PeriodosOcupacao.Add(new PeriodoOcupacao
            {
                AgendamentoId = appointment.Id, InicioUtc = Start, FimUtc = Start.AddHours(1)
            });
            acquired.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        });
        await acquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var two = Executor(second).ExecuteOnceAsync(2, "agenda:probe", async (context, token) =>
        {
            if (await Overlaps(context, Start, Start.AddHours(1), token))
                throw new SyntheticConflictException();
            context.Agendamentos.Add(Reservation());
        });

        try { await AssertWaitingForAdvisoryLock(secondPid); }
        finally { release.TrySetResult(); }
        Assert.True(await one);
        await Assert.ThrowsAsync<SyntheticConflictException>(() => two);
        Assert.Equal(1, await first.Agendamentos.CountAsync());
        Assert.Equal(1, await first.TelegramBusinessOperations.CountAsync());
        Assert.Equal(0, await second.TelegramBusinessOperations.CountAsync(o => o.UpdateId == 2));

        // Adjacency is allowed by the half-open overlap query after the lock.
        Assert.True(await Executor(second).ExecuteOnceAsync(3, "agenda:probe", async (context, token) =>
        {
            Assert.False(await Overlaps(context, Start.AddHours(1), Start.AddHours(2), token));
        }));
    }

    [PostgresFact]
    public async Task RollbackReleasesGlobalLockAndDoesNotConsumeIdempotencyKeyOrPersistAudit()
    {
        await Initialize();
        await using var db = PostgresTestDatabase.CreateContext();
        await Assert.ThrowsAsync<SyntheticConflictException>(() =>
            Executor(db).ExecuteOnceAsync(1, "agenda:rollback", async (context, token) =>
            {
                var appointment = Reservation();
                context.Agendamentos.Add(appointment);
                await context.SaveChangesAsync(token);
                context.RegistrosAuditoria.Add(new RegistroAuditoria
                {
                    AgendamentoId = appointment.Id, Operacao = "synthetic", Resultado = "success",
                    CorrelacaoId = "synthetic-79", RegistradoEmUtc = Start
                });
                await context.SaveChangesAsync(token);
                throw new SyntheticConflictException();
            }));
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(0, await db.Agendamentos.CountAsync());
        Assert.Equal(0, await db.RegistrosAuditoria.CountAsync());
        Assert.Equal(0, await db.TelegramBusinessOperations.CountAsync());

        await using var retry = PostgresTestDatabase.CreateContext();
        Assert.True(await Executor(retry).ExecuteOnceAsync(1, "agenda:rollback", (context, _) =>
        {
            context.Agendamentos.Add(Reservation());
            return Task.CompletedTask;
        }));
        Assert.False(await Executor(retry).ExecuteOnceAsync(1, "agenda:rollback", (_, _) =>
            throw new InvalidOperationException("Duplicate must not run")));
        Assert.Equal(1, await retry.Agendamentos.CountAsync());
    }

    [PostgresFact]
    public async Task StaleVersionRollsBackMutationAndIdempotencyThenAllowsFreshRetry()
    {
        await Initialize();
        await using var first = PostgresTestDatabase.CreateContext();
        first.Agendamentos.Add(Reservation());
        await first.SaveChangesAsync();
        var id = await first.Agendamentos.Select(a => a.Id).SingleAsync();
        first.ChangeTracker.Clear();
        await using var second = PostgresTestDatabase.CreateContext();
        var expected = await second.Agendamentos.AsNoTracking().Select(a => a.Versao).SingleAsync();

        Assert.True(await Executor(first).ExecuteOnceAsync(1, "agenda:edit", async (context, token) =>
        {
            var appointment = await context.Agendamentos.SingleAsync(a => a.Id == id, token);
            appointment.Observacoes = "first";
            appointment.IncrementarVersao();
        }));
        Assert.Equal(2, await second.Agendamentos.AsNoTracking().Select(a => a.Versao).SingleAsync());

        await Assert.ThrowsAsync<AgendaVersionConflictException>(() =>
            Executor(second).ExecuteOnceAsync(2, "agenda:edit", async (context, token) =>
            {
                var appointment = await context.Agendamentos.SingleAsync(a => a.Id == id, token);
                context.Entry(appointment).Property(a => a.Versao).OriginalValue = expected;
                appointment.Observacoes = "stale";
                appointment.IncrementarVersao();
            }));
        Assert.Equal("first", await second.Agendamentos.Select(a => a.Observacoes).SingleAsync());
        Assert.Equal(0, await second.TelegramBusinessOperations.CountAsync(o => o.UpdateId == 2));

        second.ChangeTracker.Clear();
        Assert.True(await Executor(second).ExecuteOnceAsync(2, "agenda:edit", async (context, token) =>
        {
            var appointment = await context.Agendamentos.SingleAsync(a => a.Id == id, token);
            context.PeriodosOcupacao.Add(new PeriodoOcupacao
            {
                AgendamentoId = id, InicioUtc = Start, FimUtc = Start.AddMinutes(30)
            });
            // Period-only mutations must explicitly increment the parent token too.
            appointment.IncrementarVersao();
        }));
        Assert.Equal(3, await second.Agendamentos.AsNoTracking().Select(a => a.Versao).SingleAsync());
    }

    [PostgresFact]
    public async Task LockTimeoutReturnsControlledFailureAndRollsBackOperationKey()
    {
        await Initialize();
        await using var blocker = PostgresTestDatabase.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await PostgresAgendaLock.AcquireAsync(blocker);
        await using var contender = PostgresTestDatabase.CreateContext();
        await Assert.ThrowsAsync<AgendaLockUnavailableException>(() =>
            Executor(contender).ExecuteOnceAsync(1, "agenda:timeout", (_, _) => Task.CompletedTask));
        Assert.Equal(0, await contender.TelegramBusinessOperations.CountAsync());
        await transaction.RollbackAsync();
        Assert.True(await Executor(contender).ExecuteOnceAsync(1, "agenda:timeout", (_, _) => Task.CompletedTask));
    }

    [PostgresFact]
    public async Task LockRequiresReadCommittedTransactionAndExecutorRejectsNestedTransactions()
    {
        await Initialize();
        await using var db = PostgresTestDatabase.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresAgendaLock.AcquireAsync(db));
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresAgendaLock.AcquireAsync(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Executor(db).ExecuteOnceAsync(1, "agenda:nested", (_, _) => Task.CompletedTask));
            await transaction.RollbackAsync();
        }
        Assert.Equal(0, await db.TelegramBusinessOperations.CountAsync());
    }

    private static AgendaMutationExecutor Executor(IanDbContext db) => new(new TelegramBusinessOperationExecutor(db));

    private static async Task Initialize()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
        db.Clientes.Add(new Cliente { Nome = "Synthetic", TelefoneNormalizado = "synthetic" });
        db.TiposServico.Add(new TipoServico
        {
            Nome = "Synthetic", DuracaoPadrao = 60, UnidadeDuracaoPadrao = UnidadeDuracao.Minutos,
            ModalidadePadrao = ModalidadeServico.Interno
        });
        for (var id = 1; id <= 3; id++)
            db.TelegramUpdatesInbox.Add(new TelegramUpdateInbox
            {
                UpdateId = id, PayloadJson = $$"""{"update_id":{{id}}}""",
                Estado = EstadoTelegramUpdate.Received, RecebidoEmUtc = Start
            });
        await db.SaveChangesAsync();
    }

    private static Agendamento Reservation() => new()
    {
        ClienteId = 1, TipoServicoId = 1, Modalidade = ModalidadeServico.Interno,
        Duracao = 60, UnidadeDuracao = UnidadeDuracao.Minutos,
        InicioPrevistoUtc = Start, FimPrevistoUtc = Start.AddHours(1)
    };

    private static Task<bool> Overlaps(IanDbContext db, DateTimeOffset start, DateTimeOffset end, CancellationToken token) =>
        (from period in db.PeriodosOcupacao
         join appointment in db.Agendamentos on period.AgendamentoId equals appointment.Id
         where appointment.Estado == EstadoAgendamento.Agendado &&
             period.InicioUtc < end && start < period.FimUtc
         select period.Id).AnyAsync(token);

    private static async Task AssertWaitingForAdvisoryLock(int pid)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(4))
        {
            if (await SingleCalendarMigrationTests.Scalar(
                $"SELECT count(*) FROM pg_stat_activity WHERE pid={pid} AND wait_event='advisory'") == 1)
                return;
            await Task.Delay(20);
        }
        Assert.Fail("A segunda conexão não foi observada aguardando o advisory lock.");
    }

    private sealed class SyntheticConflictException : Exception;
}
