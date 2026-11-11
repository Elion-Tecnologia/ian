using System.Text.Json;
using Ian.Application.Conversation;
using Ian.Domain.Entities;
using Ian.Infrastructure;
using Ian.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ian.Tests;

public sealed class PendingConversationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RetainsCollectedAndMissingFieldsAcrossInterleavedReadTurns()
    {
        var service = new PendingConversationService(new MemoryRepository());
        var started = await service.StartAsync(42, PendingIntent.CriarAgendamento,
            new Dictionary<string, string> { ["clienteNome"] = "Ana" }, ["telefone", "data"], false, Now);
        Assert.Equal(PendingIntentStartStatus.Started, started.Status);
        Assert.Equal(PendingIntentUpdateStatus.Updated, await service.UpdateAsync(42,
            new Dictionary<string, string> { ["telefone"] = "11999999999" }, ["data"], null, Now.AddMinutes(1)));

        // A consultation turn only reads this context; it does not replace or clear it.
        var afterQuery = await service.GetForTurnAsync(42, Now.AddMinutes(2));
        var followingTurn = await service.GetForTurnAsync(42, Now.AddMinutes(3));
        Assert.NotNull(afterQuery);
        Assert.Equal(started.State!.Id, afterQuery.Id);
        Assert.Equal(afterQuery.Id, followingTurn!.Id);
        Assert.Equal("Ana", followingTurn!.CollectedFields["clienteNome"]);
        Assert.Equal("11999999999", followingTurn.CollectedFields["telefone"]);
        Assert.Equal(new[] { "data" }, followingTurn.MissingFields);
    }

    [Fact]
    public async Task ReplacingAnIntentRequiresExplicitConfirmation()
    {
        var service = new PendingConversationService(new MemoryRepository());
        var first = await service.StartAsync(42, PendingIntent.CadastrarCliente, null, ["nome", "telefone"], false, Now);
        var refused = await service.StartAsync(42, PendingIntent.CriarAgendamento, null, ["data"], false, Now.AddMinutes(1));
        Assert.Equal(PendingIntentStartStatus.ReplacementConfirmationRequired, refused.Status);
        Assert.Equal(first.State!.Id, refused.State!.Id);

        var replaced = await service.StartAsync(42, PendingIntent.CriarAgendamento, null, ["data"], true, Now.AddMinutes(2));
        Assert.Equal(PendingIntentStartStatus.Started, replaced.Status);
        Assert.NotEqual(first.State.Id, replaced.State!.Id);
        Assert.Equal(PendingIntent.CriarAgendamento, replaced.State.Intent);
    }

    [Fact]
    public async Task AmbiguousEntitiesRequireChoosingOneStoredCandidateId()
    {
        var service = new PendingConversationService(new MemoryRepository());
        await service.StartAsync(42, PendingIntent.CriarAgendamento, null, ["clienteId"], false, Now);
        var candidates = new Dictionary<string, IReadOnlyList<long>> { ["cliente"] = [101, 202] };
        Assert.Equal(PendingIntentUpdateStatus.Updated,
            await service.UpdateAsync(42, new Dictionary<string, string>(), ["clienteId"], candidates, Now.AddMinutes(1)));
        Assert.Equal(PendingIntentUpdateStatus.InvalidCandidate,
            await service.SelectCandidateAsync(42, "cliente", 303, Now.AddMinutes(2)));

        Assert.Equal(PendingIntentUpdateStatus.Updated,
            await service.SelectCandidateAsync(42, "cliente", 202, Now.AddMinutes(3)));
        var state = await service.GetForTurnAsync(42, Now.AddMinutes(3));
        Assert.Equal("202", state!.CollectedFields["clienteId"]);
        Assert.Empty(state.MissingFields);
        Assert.Empty(state.CandidateIds);
    }

    [Fact]
    public async Task ExpirationRemovesContextFromActiveTurnAndAllowsNewIntent()
    {
        var repository = new MemoryRepository();
        var service = new PendingConversationService(repository);
        await service.StartAsync(42, PendingIntent.AlterarAgendamento, null, ["agendamentoId"], false, Now);

        Assert.Null(await service.GetForTurnAsync(42, Now.Add(PendingConversationService.Lifetime).AddTicks(1)));
        var next = await service.StartAsync(42, PendingIntent.CadastrarCliente, null, ["nome"], false,
            Now.Add(PendingConversationService.Lifetime).AddMinutes(1));
        Assert.Equal(PendingIntentStartStatus.Started, next.Status);
        Assert.Equal(PendingIntent.CadastrarCliente, next.State!.Intent);
    }

    [Fact]
    public async Task CancellationIsExplicitAndLeavesTheRequestInactive()
    {
        var service = new PendingConversationService(new MemoryRepository());
        await service.StartAsync(42, PendingIntent.CriarAgendamento, null, ["data"], false, Now);

        Assert.Equal(PendingIntentUpdateStatus.NotFound, await service.CancelAsync(43, Now.AddMinutes(1)));
        Assert.NotNull(await service.GetForTurnAsync(42, Now.AddMinutes(1)));
        Assert.Equal(PendingIntentUpdateStatus.Cancelled, await service.CancelAsync(42, Now.AddMinutes(2)));
        Assert.Null(await service.GetForTurnAsync(42, Now.AddMinutes(2)));
    }

    [Fact]
    public async Task RejectsInvalidAndOversizedContextWithoutSavingIt()
    {
        var repository = new MemoryRepository();
        var service = new PendingConversationService(repository);
        var invalid = await service.StartAsync(0, PendingIntent.CadastrarCliente, null, null, false, Now);
        var oversized = await service.StartAsync(42, PendingIntent.CadastrarCliente,
            Enumerable.Range(0, 7).ToDictionary(index => $"campo{index}", _ => new string('x', 2_000)),
            [], false, Now);
        Assert.Equal(PendingIntentStartStatus.Invalid, invalid.Status);
        Assert.Equal(PendingIntentStartStatus.Invalid, oversized.Status);
        Assert.Null(await service.GetForTurnAsync(42, Now));
    }

    [Fact]
    public void ConversationServiceAndPostgresRepositoryAreRegisteredInInfrastructureDi()
    {
        var configuration = new ConfigurationManager
        {
            ["Telegram:BotToken"] = "synthetic-token",
            ["Telegram:AdministratorUserId"] = "42",
            ["Telegram:WebhookSecret"] = "SYNTHETIC_SECRET",
            ["ConnectionStrings:Default"] = "Host=localhost;Database=ian_t21_tests;Username=test;Password=test"
        };
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<PendingConversationService>(scope.ServiceProvider.GetRequiredService<IPendingConversation>());
        Assert.IsType<PostgresPendingConversationRepository>(scope.ServiceProvider.GetRequiredService<IPendingConversationRepository>());
    }

    private sealed class MemoryRepository : IPendingConversationRepository
    {
        private readonly List<SolicitacaoPendente> _requests = [];
        private long _nextId;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public Task<PendingIntentRepositoryStartResult> StartAsync(SolicitacaoPendente request, bool replacementConfirmed,
            DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        {
            var current = _requests.SingleOrDefault(item => item.TelegramUserId == request.TelegramUserId && item.Estado == EstadoSolicitacaoPendente.Pendente);
            if (current is not null && current.ExpiraEmUtc <= nowUtc) current.Estado = EstadoSolicitacaoPendente.Expirada;
            else if (current is not null && !replacementConfirmed)
                return Task.FromResult(new PendingIntentRepositoryStartResult(PendingIntentStartStatus.ReplacementConfirmationRequired, current));
            else if (current is not null) current.Estado = EstadoSolicitacaoPendente.Cancelada;

            request.Id = ++_nextId;
            _requests.Add(request);
            return Task.FromResult(new PendingIntentRepositoryStartResult(PendingIntentStartStatus.Started, request));
        }

        public Task<SolicitacaoPendente?> GetActiveAsync(long userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        {
            var request = _requests.SingleOrDefault(item => item.TelegramUserId == userId && item.Estado == EstadoSolicitacaoPendente.Pendente);
            if (request is not null && request.ExpiraEmUtc <= nowUtc)
            {
                request.Estado = EstadoSolicitacaoPendente.Expirada;
                request.AtualizadaEmUtc = nowUtc;
                request = null;
            }
            return Task.FromResult(request);
        }

        public Task<PendingIntentUpdateStatus> UpdateAsync(long userId, long requestId, string collected, string missing,
            string candidates, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        {
            var current = Active(userId, requestId, nowUtc, out var status);
            if (status == PendingIntentUpdateStatus.Updated)
            {
                current!.DadosColetadosJson = collected;
                current.CamposFaltantesJson = missing;
                current.CandidatosJson = candidates;
                current.AtualizadaEmUtc = nowUtc;
            }
            return Task.FromResult(status);
        }

        public Task<PendingIntentUpdateStatus> SelectCandidateAsync(long userId, long requestId, string field, long candidateId,
            DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        {
            var current = Active(userId, requestId, nowUtc, out var status);
            if (status != PendingIntentUpdateStatus.Updated) return Task.FromResult(status);
            var candidates = JsonSerializer.Deserialize<Dictionary<string, List<long>>>(current!.CandidatosJson, JsonOptions)!;
            if (!candidates.TryGetValue(field, out var ids) || !ids.Contains(candidateId))
                return Task.FromResult(PendingIntentUpdateStatus.InvalidCandidate);
            var collected = JsonSerializer.Deserialize<Dictionary<string, string>>(current.DadosColetadosJson, JsonOptions)!;
            collected[$"{field}Id"] = candidateId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            candidates.Remove(field);
            var missing = JsonSerializer.Deserialize<List<string>>(current.CamposFaltantesJson, JsonOptions)!;
            missing.RemoveAll(name => name == field || name == $"{field}Id");
            current.DadosColetadosJson = JsonSerializer.Serialize(collected, JsonOptions);
            current.CandidatosJson = JsonSerializer.Serialize(candidates, JsonOptions);
            current.CamposFaltantesJson = JsonSerializer.Serialize(missing, JsonOptions);
            current.AtualizadaEmUtc = nowUtc;
            return Task.FromResult(PendingIntentUpdateStatus.Updated);
        }

        public Task<PendingIntentUpdateStatus> CancelAsync(long userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        {
            var current = _requests.SingleOrDefault(item => item.TelegramUserId == userId && item.Estado == EstadoSolicitacaoPendente.Pendente);
            if (current is null) return Task.FromResult(PendingIntentUpdateStatus.NotFound);
            current.Estado = current.ExpiraEmUtc <= nowUtc ? EstadoSolicitacaoPendente.Expirada : EstadoSolicitacaoPendente.Cancelada;
            current.AtualizadaEmUtc = nowUtc;
            return Task.FromResult(current.Estado == EstadoSolicitacaoPendente.Expirada ? PendingIntentUpdateStatus.Expired : PendingIntentUpdateStatus.Cancelled);
        }

        private SolicitacaoPendente? Active(long userId, long id, DateTimeOffset now, out PendingIntentUpdateStatus status)
        {
            var request = _requests.SingleOrDefault(item => item.Id == id && item.TelegramUserId == userId && item.Estado == EstadoSolicitacaoPendente.Pendente);
            if (request is null) { status = PendingIntentUpdateStatus.NotFound; return null; }
            if (request.ExpiraEmUtc <= now)
            {
                request.Estado = EstadoSolicitacaoPendente.Expirada;
                request.AtualizadaEmUtc = now;
                status = PendingIntentUpdateStatus.Expired;
                return null;
            }
            status = PendingIntentUpdateStatus.Updated;
            return request;
        }
    }
}
