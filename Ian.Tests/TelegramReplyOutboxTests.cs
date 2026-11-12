using Ian.Application.Telegram;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresFact]
    public async Task OutboxFalhaTransitoriaComprovadamenteAntesDaChamadaPodeRetomar()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.Sender = new BeforeDispatchFailure();
        f.Restart();
        await f.QueueReplyAsync(1);
        Assert.True(await f.DeliverAsync());
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var turn = await db.Set<TelegramAgentTurn>().SingleAsync();
            Assert.Equal("Failed", turn.SendState);
            Assert.Equal(f.Clock.GetUtcNow().AddSeconds(5), turn.NextSendAttemptAtUtc);
            Assert.Equal(0, (await db.Set<TelegramReplyAttempt>().SingleAsync()).TransportAttempts);
        }
        Assert.Equal(0, f.Transport.Calls);
        f.Faults.Sender = null;
        f.Restart();
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await f.DeliverAsync());
        Assert.Single(f.Transport.Replies);
    }

    private sealed class BeforeDispatchFailure : ITelegramTextSender
    {
        public Task<TelegramTextSendResult> SendAsync(TelegramInboxUpdate source, string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TelegramTextSendResult(TelegramTextSendStatus.TransportFailure, 0));
    }

    [PostgresFact]
    public async Task OutboxRateLimitPersistePrazoERetomaAposReinicioSemReexecutarNegocio()
    {
        await using var f = await Fixture.CreateAsync();
        f.Transport.Failure = "rate";
        f.Transport.RetryAfter = 90;
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(1, "Crie montagem.");
        var modelCalls = f.Model.Calls;
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            var turn = await db.Set<TelegramAgentTurn>().SingleAsync();
            Assert.Equal("private-reply:1:42", turn.LogicalReplyKey);
            Assert.Equal("Failed", turn.SendState);
            Assert.Equal(f.Clock.GetUtcNow().AddSeconds(90), turn.NextSendAttemptAtUtc);
            Assert.Equal(EstadoTelegramUpdate.Completed, (await db.TelegramUpdatesInbox.SingleAsync()).Estado);
            var attempt = await db.Set<TelegramReplyAttempt>().SingleAsync();
            Assert.Equal(90, attempt.RetryAfterSeconds);
            Assert.Equal(1, attempt.TransportAttempts);
            Assert.False(attempt.DeliveryUncertain);
        }
        f.Restart();
        f.Transport.Failure = null;
        f.Clock.Advance(TimeSpan.FromSeconds(89));
        Assert.False(await f.DeliverAsync());
        await f.HandleAgainAsync(1);
        Assert.Equal(1, f.Transport.Calls);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await f.DeliverAsync());
        Assert.Equal(modelCalls, f.Model.Calls);
        Assert.Contains("Agendamento criado", Assert.Single(f.Transport.Replies));
        Assert.False(await f.DeliverAsync());
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Single(await check.Agendamentos.ToListAsync());
        Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
        var accepted = await check.Set<TelegramAgentTurn>().SingleAsync();
        Assert.Equal("Accepted", accepted.SendState);
        Assert.Equal(123, accepted.TelegramMessageId);
        Assert.Null(accepted.NextSendAttemptAtUtc);
        Assert.Null(accepted.SendToken);
        var attempts = await check.Set<TelegramReplyAttempt>().OrderBy(a => a.Number).ToListAsync();
        Assert.Equal(new[] { "RateLimited", "Accepted" }, attempts.Select(a => a.Status));
        Assert.Equal(123, attempts[1].TelegramMessageId);
        Assert.All(attempts, a => Assert.NotNull(a.FinishedAtUtc));
    }

    [PostgresFact]
    public async Task OutboxBackoffSemRetryAfterEOrcamentoSaoDuraveis()
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1);
        f.Transport.Failure = "rate";
        foreach (var delay in new[] { 5, 10 })
        {
            Assert.True(await f.DeliverAsync());
            f.Restart();
            f.Clock.Advance(TimeSpan.FromSeconds(delay - 1));
            Assert.False(await f.DeliverAsync());
            f.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.True(await f.DeliverAsync());
        f.Restart();
        f.Clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await f.DeliverAsync());
        Assert.Equal(3, f.Transport.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        var turn = await db.Set<TelegramAgentTurn>().SingleAsync();
        Assert.Equal("Failed", turn.SendState);
        Assert.Null(turn.NextSendAttemptAtUtc);
        Assert.Equal(3, await db.Set<TelegramReplyAttempt>().CountAsync());
        Assert.Empty(f.Transport.Replies);
    }

    [PostgresFact]
    public async Task OutboxRetryAfterBloqueiaRespostasPosterioresMesmoAoEsgotarOrcamento()
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1, "Primeira resposta.");
        await f.QueueReplyAsync(2, "Segunda resposta.");
        f.Transport.Failure = "rate";
        f.Transport.RetryAfter = 60;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(await f.DeliverAsync());
            if (attempt < 3) f.Clock.Advance(TimeSpan.FromSeconds(60));
        }
        f.Restart();
        f.Transport.Failure = null;
        f.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(await f.DeliverAsync());
        Assert.Equal(3, f.Transport.Calls);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await f.DeliverAsync());
        Assert.Equal("Segunda resposta.", Assert.Single(f.Transport.Replies));
    }

    [PostgresTheory]
    [InlineData("uncertain", "TransportFailure")]
    [InlineData("timeout", "Timeout")]
    [InlineData("server", "ServerFailure")]
    [InlineData("invalid", "InvalidResponse")]
    [InlineData("reject", "Rejected")]
    public async Task OutboxNaoRepeteEnvioIncertoOuRecusaPermanente(string failure, string status)
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1);
        f.Transport.Failure = failure;
        Assert.True(await f.DeliverAsync());
        f.Transport.Failure = null;
        f.Restart();
        f.Clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await f.DeliverAsync());
        await f.HandleAgainAsync(1);
        Assert.Equal(1, f.Transport.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        var turn = await db.Set<TelegramAgentTurn>().SingleAsync();
        Assert.Equal(failure == "reject" ? "Failed" : "Uncertain", turn.SendState);
        Assert.Null(turn.TelegramMessageId);
        Assert.Null(turn.NextSendAttemptAtUtc);
        var attempt = await db.Set<TelegramReplyAttempt>().SingleAsync();
        Assert.Equal(status, attempt.Status);
        Assert.Equal(failure != "reject", attempt.DeliveryUncertain);
        Assert.Empty(f.Transport.Replies);
    }

    [PostgresFact]
    public async Task OutboxFalhaAposAceitacaoAntesDaPersistenciaNaoDuplicaEnvio()
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1);
        f.Faults.FailSendFinish = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.DeliverAsync());
        Assert.Single(f.Transport.Replies); // Telegram accepted, but there is no durable acknowledgement yet.
        f.Restart();
        Assert.False(await f.DeliverAsync());
        f.Clock.Advance(TelegramReplyDeliveryPolicy.LeaseDuration);
        Assert.False(await f.DeliverAsync());
        Assert.Equal(1, f.Transport.Calls);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal("Uncertain", (await db.Set<TelegramAgentTurn>().SingleAsync()).SendState);
        var attempt = await db.Set<TelegramReplyAttempt>().SingleAsync();
        Assert.Equal("Interrupted", attempt.Status);
        Assert.True(attempt.DeliveryUncertain);
        Assert.Null(attempt.TelegramMessageId);
    }

    [PostgresFact]
    public async Task OutboxConcorrenciaNaoDuplicaEChavesDistinguemMensagensComTextoIgual()
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1);
        await f.QueueReplyAsync(1, "Não deve substituir a resposta congelada.");
        await f.QueueReplyAsync(2);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.BeforeReply = async _ => { started.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); };
        var first = f.DeliverAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.All(await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => f.DeliverAsync())), sent => Assert.False(sent)); }
        finally { release.TrySetResult(); }
        Assert.True(await first);
        f.Transport.BeforeReply = null;
        Assert.True(await f.DeliverAsync());
        Assert.False(await f.DeliverAsync());
        Assert.Equal(new[] { "Resposta fictícia.", "Resposta fictícia." }, f.Transport.Replies);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(new[] { "private-reply:1:42", "private-reply:2:42" },
            await db.Set<TelegramAgentTurn>().OrderBy(t => t.UpdateId).Select(t => t.LogicalReplyKey).ToListAsync());
        Assert.Equal(2, await db.Set<TelegramReplyAttempt>().CountAsync());
        Assert.Equal(0, f.Model.Calls);
    }

    [PostgresTheory]
    [InlineData("administrator")]
    [InlineData("group")]
    [InlineData("actor")]
    [InlineData("media")]
    [InlineData("update")]
    public async Task OutboxRevalidaDestinatarioETipoAntesDeCadaEnvio(string change)
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1);
        if (change == "administrator") { f.Faults.AdministratorUserId = 43; f.Restart(); }
        else
        {
            var payload = change switch
            {
                "group" => """{"update_id":1,"message":{"from":{"id":42},"chat":{"id":42,"type":"group"},"text":"Fictício"}}""",
                "actor" => """{"update_id":1,"message":{"from":{"id":43},"chat":{"id":42,"type":"private"},"text":"Fictício"}}""",
                "media" => """{"update_id":1,"message":{"from":{"id":42},"chat":{"id":42,"type":"private"},"photo":[]}}""",
                _ => """{"update_id":2,"message":{"from":{"id":42},"chat":{"id":42,"type":"private"},"text":"Fictício"}}"""
            };
            await using var db = PostgresTestDatabase.CreateContext();
            await db.TelegramUpdatesInbox.Where(u => u.UpdateId == 1).ExecuteUpdateAsync(s => s.SetProperty(u => u.PayloadJson, payload));
        }
        Assert.True(await f.DeliverAsync());
        Assert.False(await f.DeliverAsync());
        Assert.Equal(0, f.Transport.Calls);
        await using var check = PostgresTestDatabase.CreateContext();
        var attempt = await check.Set<TelegramReplyAttempt>().SingleAsync();
        Assert.Equal("UnauthorizedRecipient", attempt.Status);
        Assert.Equal(0, attempt.TransportAttempts);
    }

    [PostgresFact]
    public async Task OutboxResultadoTardioExigeTokenEFinalizacaoIdempotente()
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1);
        await using var db = PostgresTestDatabase.CreateContext();
        var store = new PostgresTelegramReplyOutbox(db);
        var claim = Assert.IsType<TelegramReplyClaim>(await store.ClaimAsync(f.Clock.GetUtcNow(), null, default));
        var result = new TelegramTextSendResult(TelegramTextSendStatus.Accepted, 1, 123);
        Assert.False(await store.FinishAsync(claim with { Token = Guid.NewGuid() }, result, f.Clock.GetUtcNow(), default));
        f.Clock.Advance(TelegramReplyDeliveryPolicy.LeaseDuration);
        Assert.Null(await store.ClaimAsync(f.Clock.GetUtcNow(), null, default));
        Assert.Equal("Uncertain", (await db.Set<TelegramAgentTurn>().AsNoTracking().SingleAsync()).SendState);
        Assert.True(await store.FinishAsync(claim, result, f.Clock.GetUtcNow(), default));
        Assert.False(await store.FinishAsync(claim, new(TelegramTextSendStatus.RateLimited, 1), f.Clock.GetUtcNow(), default));
        Assert.Equal("Accepted", (await db.Set<TelegramAgentTurn>().AsNoTracking().SingleAsync()).SendState);
        Assert.Equal("Accepted", (await db.Set<TelegramReplyAttempt>().AsNoTracking().SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task OutboxClaimEFinalizacaoPossuemRollbackAtomico()
    {
        await using var f = await Fixture.CreateAsync();
        await f.QueueReplyAsync(1);
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_reply_attempt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic attempt failure'; END $$;
            CREATE TRIGGER fail_reply_attempt BEFORE INSERT ON telegram_reply_attempts
            FOR EACH ROW EXECUTE FUNCTION fail_reply_attempt();
            """);
        var store = new PostgresTelegramReplyOutbox(db);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.ClaimAsync(f.Clock.GetUtcNow(), null, default));
        var pending = await db.Set<TelegramAgentTurn>().AsNoTracking().SingleAsync();
        Assert.Equal("Pending", pending.SendState);
        Assert.Equal(0, pending.SendAttempts);
        Assert.Null(pending.SendToken);
        Assert.Empty(await db.Set<TelegramReplyAttempt>().AsNoTracking().ToListAsync());
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER fail_reply_attempt ON telegram_reply_attempts;
            CREATE TRIGGER fail_reply_attempt BEFORE UPDATE ON telegram_reply_attempts
            FOR EACH ROW EXECUTE FUNCTION fail_reply_attempt();
            """);
        var claim = Assert.IsType<TelegramReplyClaim>(await store.ClaimAsync(f.Clock.GetUtcNow(), null, default));
        var accepted = new TelegramTextSendResult(TelegramTextSendStatus.Accepted, 1, 123);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.FinishAsync(claim, accepted, f.Clock.GetUtcNow(), default));
        var sending = await db.Set<TelegramAgentTurn>().AsNoTracking().SingleAsync();
        Assert.Equal("Sending", sending.SendState);
        Assert.Null(sending.TelegramMessageId);
        Assert.Null((await db.Set<TelegramReplyAttempt>().AsNoTracking().SingleAsync()).Status);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_reply_attempt ON telegram_reply_attempts;");
        Assert.True(await store.FinishAsync(claim, accepted, f.Clock.GetUtcNow(), default));
    }

    [PostgresFact]
    public async Task OutboxMigrationPreservaRespostasERecusaRollbackComDados()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var migrator = db.GetService<IMigrator>();
        const string previous = "20261112022359_PersistirTurnosAgente";
        await migrator.MigrateAsync(previous);
        db.Set<EnvioTelegram>().Add(new EnvioTelegram
        {
            DestinoChatId = 42, TipoMensagem = "synthetic-legacy", Estado = EstadoEnvioTelegram.Enviado,
            Tentativas = 1, CriadoEmUtc = DateTimeOffset.UtcNow, TelegramMessageId = 456
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO telegram_updates_inbox ("UpdateId", "PayloadJson", "Estado", "RecebidoEmUtc", "Tentativas")
            SELECT n, '{{"update_id":1}}', 'Completed', now(), 1 FROM generate_series(1,6) n;
            INSERT INTO telegram_agent_turns ("UpdateId", "TelegramUserId", "ResponseText", "SendState", "SendAttempts", "SendStatus", "TelegramMessageId")
            VALUES (1,42,'Pendente','Pending',0,NULL,NULL), (2,42,'Recusada','Failed',1,'RateLimited',NULL),
                (3,42,'Interrompida','Sending',1,NULL,NULL), (4,42,'Aceita','Accepted',1,'Accepted',123),
                (5,42,'Incerta','Uncertain',1,'Timeout',NULL), (6,42,'Definitiva','Failed',1,'Rejected',NULL);
            """);
        await migrator.MigrateAsync();
        var turns = await db.Set<TelegramAgentTurn>().AsNoTracking().OrderBy(t => t.UpdateId).ToListAsync();
        Assert.Equal(new[] { "Pendente", "Recusada", "Interrompida", "Aceita", "Incerta", "Definitiva" }, turns.Select(t => t.ResponseText));
        Assert.Equal(new[] { 1L, 2L }, turns.Where(t => t.NextSendAttemptAtUtc != null).Select(t => t.UpdateId));
        Assert.Equal("Uncertain", turns[2].SendState);
        Assert.Equal(123, turns[3].TelegramMessageId);
        Assert.Equal(6, turns.Select(t => t.LogicalReplyKey).Distinct().Count());
        var legacy = await db.Set<EnvioTelegram>().AsNoTracking().SingleAsync();
        Assert.Equal("synthetic-legacy", legacy.TipoMensagem);
        Assert.Equal(EstadoEnvioTelegram.Enviado, legacy.Estado);
        Assert.Equal(456, legacy.TelegramMessageId);
        Assert.Empty(await db.Set<TelegramReplyAttempt>().ToListAsync());
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Rollback recusado", error.MessageText);
        Assert.Equal(6, await db.Set<TelegramAgentTurn>().CountAsync());
    }
}
