using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Ian.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class PendingConversationPostgresTests
{
    [PostgresFact]
    public async Task ConversationIsRecoveredByANewServiceAndDbContextAfterRestart()
    {
        var userId = Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
        var now = DateTimeOffset.UtcNow;
        long requestId;
        await using (var firstDb = PostgresTestDatabase.CreateContext())
        {
            await firstDb.Database.MigrateAsync();
            var firstService = CreateService(firstDb, userId);
            var denied = await firstService.StartAsync(userId + 1, PendingIntent.CriarAgendamento, null, ["data"], false, now);
            Assert.Equal(PendingIntentStartStatus.Invalid, denied.Status);
            var start = await firstService.StartAsync(userId, PendingIntent.CriarAgendamento,
                new Dictionary<string, string> { ["clienteNome"] = "Cliente teste" }, ["telefone", "data"], false, now);
            Assert.Equal(PendingIntentStartStatus.Started, start.Status);
            requestId = start.State!.Id;
        }

        await using (var restartedDb = PostgresTestDatabase.CreateContext())
        {
            var restartedService = CreateService(restartedDb, userId);
            var recovered = await restartedService.GetForTurnAsync(userId, now.AddMinutes(1));
            Assert.NotNull(recovered);
            Assert.Equal(requestId, recovered.Id);
            Assert.Equal(PendingIntent.CriarAgendamento, recovered.Intent);
            Assert.Equal("Cliente teste", recovered.CollectedFields["clienteNome"]);
            Assert.Equal(new[] { "telefone", "data" }, recovered.MissingFields);

            // A read used by an interleaved consultation leaves the mutation pending.
            Assert.Equal(recovered, await restartedService.GetForTurnAsync(userId, now.AddMinutes(2)));
            Assert.Equal(PendingIntentUpdateStatus.Updated, await restartedService.CancelAsync(userId, now.AddMinutes(2)));
        }
    }

    [PostgresFact]
    public async Task ReplacementAmbiguityAndExpirationArePersistedAsExplicitTransitions()
    {
        var userId = Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
        var now = DateTimeOffset.UtcNow;
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
        var service = CreateService(db, userId);

        var first = await service.StartAsync(userId, PendingIntent.CadastrarCliente, null, ["nome", "telefone"], false, now);
        var replacementPrompt = await service.StartAsync(userId, PendingIntent.CriarAgendamento, null, ["clienteId", "data"], false, now.AddMinutes(1));
        Assert.Equal(PendingIntentStartStatus.ReplacementConfirmationRequired, replacementPrompt.Status);
        Assert.Equal(first.State!.Id, replacementPrompt.State!.Id);

        var replacement = await service.StartAsync(userId, PendingIntent.CriarAgendamento, null, ["clienteId", "data"], true, now.AddMinutes(2));
        Assert.Equal(PendingIntentStartStatus.Started, replacement.Status);
        Assert.Equal(EstadoSolicitacaoPendente.Cancelada,
            (await db.SolicitacoesPendentes.SingleAsync(item => item.Id == first.State!.Id)).Estado);

        var candidateIds = new Dictionary<string, IReadOnlyList<long>> { ["cliente"] = [301, 302] };
        Assert.Equal(PendingIntentUpdateStatus.Updated,
            await service.UpdateAsync(userId, new Dictionary<string, string> { ["data"] = "2026-11-12" }, ["clienteId"], candidateIds, now.AddMinutes(3)));
        Assert.Equal(PendingIntentUpdateStatus.InvalidCandidate,
            await service.SelectCandidateAsync(userId, "cliente", 303, now.AddMinutes(4)));
        Assert.Equal(PendingIntentUpdateStatus.Updated,
            await service.SelectCandidateAsync(userId, "cliente", 302, now.AddMinutes(5)));

        var selected = await service.GetForTurnAsync(userId, now.AddMinutes(5));
        Assert.Equal("302", selected!.CollectedFields["clienteId"]);
        Assert.Empty(selected.MissingFields);
        Assert.Empty(selected.CandidateIds);

        Assert.Null(await service.GetForTurnAsync(userId, now.Add(PendingConversationService.Lifetime).AddMinutes(3)));
        Assert.Equal(EstadoSolicitacaoPendente.Expirada,
            (await db.SolicitacoesPendentes.SingleAsync(item => item.Id == replacement.State!.Id)).Estado);

        var next = await service.StartAsync(userId, PendingIntent.CadastrarCliente, null, ["nome"], false,
            now.Add(PendingConversationService.Lifetime).AddMinutes(4));
        Assert.Equal(PendingIntentStartStatus.Started, next.Status);
    }

    private static IPendingConversation CreateService(IanDbContext db, long userId) =>
        new PendingConversationService(new PostgresPendingConversationRepository(
            db, Options.Create(new TelegramOptions { AdministratorUserId = userId })));
}
