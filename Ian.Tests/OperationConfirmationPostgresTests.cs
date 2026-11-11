using System.Globalization;
using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Ian.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class OperationConfirmationPostgresTests
{
    [PostgresFact]
    public async Task RestartThenValidConfirmationConsumesOnceIncludingDifferentUpdates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var challenge = await fixture.PrepareAsync();
        await using var restartedDb = PostgresTestDatabase.CreateContext();
        var service = fixture.Service(restartedDb);
        var calls = 0;
        var result = await service.ExecuteAsync(fixture.Caller, fixture.UserId, fixture.Accept(challenge), async (operation, token) =>
        {
            calls++;
            var target = await restartedDb.Agendamentos.SingleAsync(item => item.Id == operation.AgendamentoId, token);
            Assert.Equal(operation.Version, target.Versao);
            target.Estado = EstadoAgendamento.Cancelado; // Test stand-in for the future #35 use case.
            target.IncrementarVersao();
            return AgentToolResult.Succeeded("Cancelado.");
        });
        Assert.Equal(AgentToolOutcome.Success, result.Outcome);
        foreach (var update in new[] { fixture.UserId, fixture.UserId + 1 })
        {
            var repeat = await service.ExecuteAsync(fixture.Caller, update, fixture.Accept(challenge), (_, _) =>
            {
                calls++;
                return Task.FromResult(AgentToolResult.Succeeded("não deve executar"));
            });
            Assert.Equal(AgentToolOutcome.Conflict, repeat.Outcome);
        }
        Assert.Equal(1, calls);
        Assert.Equal(EstadoSolicitacaoPendente.Confirmada,
            (await restartedDb.SolicitacoesPendentes.AsNoTracking().SingleAsync(item => item.Id == fixture.PendingId)).Estado);
        Assert.Equal(EstadoAgendamento.Cancelado,
            (await restartedDb.Agendamentos.AsNoTracking().SingleAsync(item => item.Id == fixture.TargetId)).Estado);
    }

    [PostgresFact]
    public async Task ExpiredChangedVersionClosedAndDeletedTargetsNeverInvokeMutation()
    {
        foreach (var scenario in new[] { "expired", "version", "closed", "deleted", "arguments", "token" })
        {
            await using var fixture = await Fixture.CreateAsync();
            var challenge = await fixture.PrepareAsync();
            var acceptance = fixture.Accept(challenge);
            var target = await fixture.Db.Agendamentos.SingleAsync(item => item.Id == fixture.TargetId);
            switch (scenario)
            {
                case "expired": fixture.Clock.Advance(TimeSpan.FromMinutes(5)); break;
                case "version": target.IncrementarVersao(); break;
                case "closed": target.Estado = EstadoAgendamento.Concluido; break;
                case "deleted": fixture.Db.Agendamentos.Remove(target); break;
                case "arguments": acceptance = acceptance with { Arguments = fixture.Arguments with { Observacoes = "mudou" } }; break;
                case "token": acceptance = acceptance with { ChallengeId = Guid.NewGuid() }; break;
            }
            await fixture.Db.SaveChangesAsync();
            var calls = 0;
            var result = await fixture.Service(fixture.Db).ExecuteAsync(fixture.Caller, fixture.UserId, acceptance, (_, _) =>
            {
                calls++;
                return Task.FromResult(AgentToolResult.Succeeded("não deve executar"));
            });
            Assert.Equal(AgentToolOutcome.Conflict, result.Outcome);
            Assert.Equal(0, calls);
            Assert.False(await fixture.Db.TelegramBusinessOperations.AnyAsync(item => item.UpdateId == fixture.UserId));
        }
    }

    [PostgresFact]
    public async Task OtherActorAndNonPrivateOrNonTextCallsCannotPrepareOrConsume()
    {
        await using var fixture = await Fixture.CreateAsync();
        var challenge = await fixture.PrepareAsync();
        foreach (var caller in new[] { fixture.Caller with { UserId = fixture.UserId + 1 },
                     fixture.Caller with { ChatId = -fixture.UserId }, fixture.Caller with { IsPrivateChat = false },
                     fixture.Caller with { IsHuman = false }, fixture.Caller with { HasText = false } })
        {
            var service = fixture.Service(fixture.Db);
            Assert.Equal(AgentToolOutcome.Error,
                (await service.PrepareAsync(caller, fixture.PendingId, fixture.Arguments)).Outcome);
            var result = await service.ExecuteAsync(caller, fixture.UserId, fixture.Accept(challenge), (_, _) =>
                throw new InvalidOperationException("Unauthorized mutation"));
            Assert.Equal(AgentToolOutcome.Error, result.Outcome);
        }
    }

    [PostgresFact]
    public async Task AmbiguityRequiresExplicitIdAndChangingContextInvalidatesTheChallenge()
    {
        await using var fixture = await Fixture.CreateAsync();
        var conversation = fixture.Conversation;
        await conversation.UpdateAsync(fixture.UserId, new Dictionary<string, string>(), ["agendamentoId"],
            new Dictionary<string, IReadOnlyList<long>> { ["agendamento"] = [fixture.TargetId, fixture.TargetId + 1] }, fixture.Clock.GetUtcNow());
        Assert.Equal(AgentToolOutcome.Ambiguous,
            (await fixture.Service(fixture.Db).PrepareAsync(fixture.Caller, fixture.PendingId, fixture.Arguments)).Outcome);
        Assert.Equal(PendingIntentUpdateStatus.InvalidCandidate,
            await conversation.SelectCandidateAsync(fixture.UserId, "agendamento", fixture.TargetId + 2, fixture.Clock.GetUtcNow()));
        Assert.Equal(PendingIntentUpdateStatus.Updated,
            await conversation.SelectCandidateAsync(fixture.UserId, "agendamento", fixture.TargetId, fixture.Clock.GetUtcNow()));
        var challenge = await fixture.PrepareAsync();
        await conversation.UpdateAsync(fixture.UserId, new Dictionary<string, string>(), [], null, fixture.Clock.GetUtcNow());
        Assert.Equal(AgentToolOutcome.Conflict,
            (await fixture.Service(fixture.Db).ExecuteAsync(fixture.Caller, fixture.UserId, fixture.Accept(challenge), (_, _) =>
                throw new InvalidOperationException("Obsolete confirmation"))).Outcome);
    }

    [PostgresFact]
    public async Task OldYesCannotConfirmNewChallengeReplacedIntentOrCancelledRequest()
    {
        foreach (var scenario in new[] { "new-challenge", "replacement", "cancel" })
        {
            await using var fixture = await Fixture.CreateAsync();
            var old = await fixture.PrepareAsync();
            switch (scenario)
            {
                case "new-challenge": await fixture.PrepareAsync(); break;
                case "replacement":
                    await fixture.Conversation.StartAsync(fixture.UserId, PendingIntent.CriarAgendamento, null, ["data"], true, fixture.Clock.GetUtcNow());
                    break;
                case "cancel": await fixture.Conversation.CancelAsync(fixture.UserId, fixture.Clock.GetUtcNow()); break;
            }
            Assert.Equal(AgentToolOutcome.Conflict,
                (await fixture.Service(fixture.Db).ExecuteAsync(fixture.Caller, fixture.UserId, fixture.Accept(old), (_, _) =>
                    throw new InvalidOperationException("Old yes"))).Outcome);
        }
    }

    [PostgresFact]
    public async Task FailedBusinessRevalidationRollsBackWritesAndLeavesChallengeAvailable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var challenge = await fixture.PrepareAsync();
        var result = await fixture.Service(fixture.Db).ExecuteAsync(fixture.Caller, fixture.UserId, fixture.Accept(challenge), async (operation, token) =>
        {
            var target = await fixture.Db.Agendamentos.SingleAsync(item => item.Id == operation.AgendamentoId, token);
            target.Estado = EstadoAgendamento.Cancelado;
            await fixture.Db.SaveChangesAsync(token);
            return AgentToolResult.ConflictResult("Revalidação de negócio falhou.");
        });
        Assert.Equal(AgentToolOutcome.Conflict, result.Outcome);
        Assert.Equal(EstadoAgendamento.Agendado,
            (await fixture.Db.Agendamentos.SingleAsync(item => item.Id == fixture.TargetId)).Estado);
        Assert.Equal(EstadoSolicitacaoPendente.Pendente,
            (await fixture.Db.SolicitacoesPendentes.SingleAsync(item => item.Id == fixture.PendingId)).Estado);
        Assert.False(await fixture.Db.TelegramBusinessOperations.AnyAsync(item => item.UpdateId == fixture.UserId));
        Assert.Equal(AgentToolOutcome.Success,
            (await fixture.Service(fixture.Db).ExecuteAsync(fixture.Caller, fixture.UserId, fixture.Accept(challenge), (_, _) =>
                Task.FromResult(AgentToolResult.Succeeded("Caso de uso autorizado.")))).Outcome);
    }

    [PostgresFact]
    public async Task DeadlineAtCommitAndExceptionsRollBackMutationAndConsumption()
    {
        foreach (var scenario in new[] { "deadline", "exception" })
        {
            await using var fixture = await Fixture.CreateAsync();
            var challenge = await fixture.PrepareAsync();
            async Task<AgentToolResult> Mutation(ConfirmedAgendaOperation operation, CancellationToken token)
            {
                var target = await fixture.Db.Agendamentos.SingleAsync(item => item.Id == operation.AgendamentoId, token);
                target.Estado = EstadoAgendamento.Cancelado;
                target.IncrementarVersao();
                await fixture.Db.SaveChangesAsync(token);
                if (scenario == "exception") throw new InvalidOperationException("Simulated failure");
                fixture.Clock.Advance(TimeSpan.FromMinutes(5));
                return AgentToolResult.Succeeded("Não pode confirmar após expirar.");
            }
            if (scenario == "exception")
                await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service(fixture.Db).ExecuteAsync(
                    fixture.Caller, fixture.UserId, fixture.Accept(challenge), Mutation));
            else
                Assert.Equal(AgentToolOutcome.Conflict, (await fixture.Service(fixture.Db).ExecuteAsync(
                    fixture.Caller, fixture.UserId, fixture.Accept(challenge), Mutation)).Outcome);
            var targetAfterFailure = await fixture.Db.Agendamentos.SingleAsync(item => item.Id == fixture.TargetId);
            Assert.Equal(EstadoAgendamento.Agendado, targetAfterFailure.Estado);
            Assert.Equal(1, targetAfterFailure.Versao);
            Assert.Equal(EstadoSolicitacaoPendente.Pendente,
                (await fixture.Db.SolicitacoesPendentes.SingleAsync(item => item.Id == fixture.PendingId)).Estado);
            Assert.False(await fixture.Db.TelegramBusinessOperations.AnyAsync(item => item.UpdateId == fixture.UserId));
        }
    }

    [PostgresFact]
    public async Task ConcurrentAcceptancesRunTheMutationOnlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var challenge = await fixture.PrepareAsync();
        var calls = 0;
        async Task<AgentToolResult> Execute(long update)
        {
            await using var db = PostgresTestDatabase.CreateContext();
            return await fixture.Service(db).ExecuteAsync(fixture.Caller, update, fixture.Accept(challenge), (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(AgentToolResult.Succeeded("Caso de uso autorizado."));
            });
        }
        var results = await Task.WhenAll(Execute(fixture.UserId), Execute(fixture.UserId + 1));
        Assert.Single(results, result => result.Outcome == AgentToolOutcome.Success);
        Assert.Single(results, result => result.Outcome == AgentToolOutcome.Conflict);
        Assert.Equal(1, calls);
    }

    [PostgresFact]
    public async Task SimpleOperationsDoNotCreateRedundantConfirmations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service(fixture.Db).PrepareAsync(fixture.Caller, fixture.PendingId,
            fixture.Arguments with { Operacao = "concluir" });
        Assert.Equal(AgentToolOutcome.Success, result.Outcome);
        Assert.Null((await fixture.Db.SolicitacoesPendentes.SingleAsync(item => item.Id == fixture.PendingId)).ConfirmacaoJson);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public IanDbContext Db { get; } = PostgresTestDatabase.CreateContext();
        public TestClock Clock { get; } = new();
        public long UserId { get; } = Random.Shared.NextInt64(1_000_000_000, 1_000_000_000_000);
        public long TargetId { get; private set; }
        public long PendingId { get; private set; }
        public AgentToolCaller Caller => new(UserId, UserId, true, true, true);
        public AlterarAgendamentoToolRequest Arguments => new(TargetId, "cancelar", null, null, null);
        public IPendingConversation Conversation => new PendingConversationService(new PostgresPendingConversationRepository(
            Db, Options.Create(new TelegramOptions { AdministratorUserId = UserId })));
        public IOperationConfirmation Service(IanDbContext db) => new PostgresOperationConfirmation(db,
            new AgendaMutationExecutor(new TelegramBusinessOperationExecutor(db)),
            Options.Create(new TelegramOptions { AdministratorUserId = UserId }), Clock);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Db.Database.MigrateAsync();
            var client = new Cliente { Nome = "Cliente confirmação", TelefoneNormalizado = fixture.UserId.ToString(CultureInfo.InvariantCulture) };
            fixture.Db.Clientes.Add(client);
            await fixture.Db.SaveChangesAsync();
            var target = new Agendamento
            {
                ClienteId = client.Id, TipoServicoId = TiposServicoPredefinidos.OrcamentoId,
                Modalidade = ModalidadeServico.Interno, Duracao = 60, UnidadeDuracao = UnidadeDuracao.Minutos,
                InicioPrevistoUtc = fixture.Clock.GetUtcNow(), FimPrevistoUtc = fixture.Clock.GetUtcNow().AddHours(1)
            };
            fixture.Db.Agendamentos.Add(target);
            foreach (var update in new[] { fixture.UserId, fixture.UserId + 1 })
                fixture.Db.TelegramUpdatesInbox.Add(new TelegramUpdateInbox
                { UpdateId = update, PayloadJson = "{}", RecebidoEmUtc = fixture.Clock.GetUtcNow() });
            await fixture.Db.SaveChangesAsync();
            fixture.TargetId = target.Id;
            var pending = await fixture.Conversation.StartAsync(fixture.UserId, PendingIntent.AlterarAgendamento,
                new Dictionary<string, string>
                { ["agendamentoId"] = target.Id.ToString(CultureInfo.InvariantCulture), ["operacao"] = "cancelar" },
                [], false, fixture.Clock.GetUtcNow());
            fixture.PendingId = pending.State!.Id;
            return fixture;
        }

        public async Task<ConfirmationChallenge> PrepareAsync()
        {
            var result = await Service(Db).PrepareAsync(Caller, PendingId, Arguments);
            Assert.Equal(AgentToolOutcome.ConfirmationRequired, result.Outcome);
            return Assert.IsType<ConfirmationChallenge>(result.Data);
        }

        public ConfirmationAcceptance Accept(ConfirmationChallenge challenge) => new(PendingId, challenge.ChallengeId, challenge.Version, Arguments);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
