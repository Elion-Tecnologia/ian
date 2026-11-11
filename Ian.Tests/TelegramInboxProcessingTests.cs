using System.Text.Json;
using Ian.Application.Telegram;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

public sealed class TelegramInboxProcessingTests
{
    private const string AuthorizedPayload =
        """{"update_id":100,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"text":"Agendar"}}""";

    private const string DeniedPayload =
        """{"update_id":101,"message":{"from":{"id":99,"is_bot":false},"chat":{"id":99,"type":"private"},"text":"Cancelar"}}""";

    [Fact]
    public async Task DuplicateUpdateIsStoredOnlyOnce()
    {
        var store = new FakeStore();

        store.Add(100, AuthorizedPayload);
        store.Add(100, AuthorizedPayload);

        Assert.Equal(1, store.Count);

        var claims = await Task.WhenAll(
            store.TryClaimAsync(TimeSpan.FromMinutes(2), default),
            store.TryClaimAsync(TimeSpan.FromMinutes(2), default));

        Assert.Single(claims, claim => claim is not null);
    }

    [Fact]
    public async Task PrivateEventWithoutCommandDoesNotCallBusinessHandler()
    {
        const string payload =
            """{"update_id":102,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"}}}""";

        var store = new FakeStore();
        store.Add(102, payload);

        var handler = new TestAuthorizedHandler();
        var consumer = CreateConsumer(store, handler);

        Assert.True(await consumer.ProcessNextAsync(default));

        Assert.Equal("Completed", store.GetState(102));
        Assert.Equal("NonCommandEvent", store.GetResult(102));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("voice", false)]
    [InlineData("voice", true)]
    [InlineData("audio", false)]
    [InlineData("audio", true)]
    public async Task MediaNeverExecutesCommandsForProjectedOrPreviouslyStoredUpdates(string media, bool project)
    {
        // A legacy inbox row can still contain media metadata after this deployment.
        var payload = JsonSerializer.Serialize(new
        {
            update_id = 103,
            message = new Dictionary<string, object>
            {
                ["message_id"] = 17,
                ["from"] = new { id = 42, is_bot = false },
                ["chat"] = new { id = 42, type = "private" },
                [media] = new { file_id = "synthetic", duration = 1 },
                ["caption"] = "Cancelar agendamento"
            }
        });
        if (project)
        {
            using var json = JsonDocument.Parse(payload);
            Assert.True(TelegramUpdateParser.TryParse(json.RootElement, out var update));
            payload = update!.PayloadJson;
        }
        var store = new FakeStore();
        store.Add(103, payload);
        var handler = new TestAuthorizedHandler();

        Assert.True(await CreateConsumer(store, handler).ProcessNextAsync(default));

        Assert.Equal("Completed", store.GetState(103));
        Assert.Equal("NonCommandEvent", store.GetResult(103));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ExpiredLeaseAllowsRecoveryAfterCrash()
    {
        var store = new FakeStore();
        store.Add(100, AuthorizedPayload);

        var originalClaim = await store.TryClaimAsync(
            TimeSpan.FromSeconds(30),
            default);

        Assert.NotNull(originalClaim);

        // Simula queda do consumidor e passagem do tempo.
        store.Advance(TimeSpan.FromSeconds(31));

        var handler = new TestAuthorizedHandler();
        var consumer = CreateConsumer(store, handler);

        Assert.True(await consumer.ProcessNextAsync(default));

        Assert.Equal("Completed", store.GetState(100));
        Assert.Equal(2, store.GetAttempts(100));
        Assert.Equal(1, handler.Calls);

        // O consumidor antigo perdeu a capacidade de confirmar.
        Assert.False(await store.CompleteAsync(
            100,
            originalClaim!.LeaseToken,
            "Authorized",
            default));
    }

    [Fact]
    public async Task TransientFailureIsRetriedAndCompleted()
    {
        var store = new FakeStore();
        store.Add(100, AuthorizedPayload);

        var handler = new TestAuthorizedHandler
        {
            FailOnce = true
        };

        var consumer = CreateConsumer(store, handler);

        Assert.True(await consumer.ProcessNextAsync(default));

        Assert.Equal("FailedRetryable", store.GetState(100));
        Assert.Equal("ProcessingFailed", store.GetError(100));

        // O retry não pode ocorrer antes do horário previsto.
        Assert.False(await consumer.ProcessNextAsync(default));

        store.Advance(TimeSpan.FromSeconds(11));

        Assert.True(await consumer.ProcessNextAsync(default));

        Assert.Equal("Completed", store.GetState(100));
        Assert.Equal(2, store.GetAttempts(100));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task DeniedMessageCompletesWithoutBusinessHandler()
    {
        var store = new FakeStore();
        store.Add(101, DeniedPayload);

        var handler = new TestAuthorizedHandler();
        var consumer = CreateConsumer(store, handler);

        Assert.True(await consumer.ProcessNextAsync(default));

        Assert.Equal("Completed", store.GetState(101));
        Assert.Equal("Denied", store.GetResult(101));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MissingHandlerPausesWithoutDiscardingUpdate()
    {
        var store = new FakeStore();
        store.Add(100, AuthorizedPayload);

        var deferred = new DeferredTelegramHandlers();

        var dispatcher = new TelegramUpdateDispatcher(
            deferred,
            deferred);

        var consumer = new TelegramInboxConsumer(
            store,
            dispatcher,
            Options.Create(new TelegramOptions
            {
                AdministratorUserId = 42
            }),
            Options.Create(new TelegramInboxProcessingOptions()),
            NullLogger<TelegramInboxConsumer>.Instance);

        Assert.True(await consumer.ProcessNextAsync(default));

        Assert.Equal("FailedPaused", store.GetState(100));
        Assert.Equal("HandlerUnavailable", store.GetError(100));

        Assert.False(await consumer.ProcessNextAsync(default));
        Assert.Equal(1, store.Count);
    }

    private static TelegramInboxConsumer CreateConsumer(
        FakeStore store,
        ITelegramAuthorizedUpdateHandler handler)
    {
        var dispatcher = new TelegramUpdateDispatcher(
            handler,
            new TestGroupHandler());

        return new TelegramInboxConsumer(
            store,
            dispatcher,
            Options.Create(new TelegramOptions
            {
                AdministratorUserId = 42
            }),
            Options.Create(new TelegramInboxProcessingOptions()),
            NullLogger<TelegramInboxConsumer>.Instance);
    }

    private sealed class TestAuthorizedHandler :
        ITelegramAuthorizedUpdateHandler
    {
        public int Calls { get; private set; }
        public bool FailOnce { get; set; }

        public Task HandleAsync(
            TelegramInboxUpdate update,
            CancellationToken cancellationToken)
        {
            Calls++;

            if (FailOnce)
            {
                FailOnce = false;
                throw new InvalidOperationException("Falha simulada.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestGroupHandler : ITelegramGroupEventHandler
    {
        public Task HandleCandidateAsync(
            TelegramGroupEventCandidate candidate,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : ITelegramInboxProcessingStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<long, Entry> _entries = new();

        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public int Count
        {
            get
            {
                lock (_gate)
                    return _entries.Count;
            }
        }

        public void Add(long id, string payload)
        {
            lock (_gate)
            {
                _entries.TryAdd(id, new Entry
                {
                    Update = new TelegramInboxUpdate(id, payload)
                });
            }
        }

        public void Advance(TimeSpan time)
        {
            lock (_gate)
                _now += time;
        }

        public string GetState(long id)
        {
            lock (_gate)
                return _entries[id].State;
        }

        public int GetAttempts(long id)
        {
            lock (_gate)
                return _entries[id].Attempts;
        }

        public string? GetError(long id)
        {
            lock (_gate)
                return _entries[id].Error;
        }

        public string? GetResult(long id)
        {
            lock (_gate)
                return _entries[id].Result;
        }

        public Task<TelegramClaimedUpdate?> TryClaimAsync(
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var entry = _entries.Values.FirstOrDefault(item =>
                    item.State == "Received"
                    || (item.State == "FailedRetryable"
                        && item.NextRetry <= _now)
                    || (item.State == "Processing"
                        && item.LeaseUntil <= _now));

                if (entry is null)
                {
                    return Task.FromResult<TelegramClaimedUpdate?>(null);
                }

                entry.State = "Processing";
                entry.Attempts++;
                entry.Token = Guid.NewGuid();
                entry.LeaseUntil = _now + leaseDuration;
                entry.NextRetry = null;
                entry.Error = null;

                return Task.FromResult<TelegramClaimedUpdate?>(
                    new TelegramClaimedUpdate(
                        entry.Update,
                        entry.Token.Value,
                        entry.Attempts));
            }
        }

        public Task<bool> RenewLeaseAsync(
            long updateId,
            Guid leaseToken,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!OwnsLease(updateId, leaseToken))
                    return Task.FromResult(false);

                _entries[updateId].LeaseUntil =
                    _now + leaseDuration;

                return Task.FromResult(true);
            }
        }

        public Task<bool> CompleteAsync(
            long updateId,
            Guid leaseToken,
            string result,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!OwnsLease(updateId, leaseToken))
                    return Task.FromResult(false);

                var entry = _entries[updateId];

                entry.State = "Completed";
                entry.Result = result;
                entry.Token = null;
                entry.LeaseUntil = null;

                return Task.FromResult(true);
            }
        }

        public Task<bool> FailAsync(
            long updateId,
            Guid leaseToken,
            TimeSpan? retryDelay,
            string errorCode,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!OwnsLease(updateId, leaseToken))
                    return Task.FromResult(false);

                var entry = _entries[updateId];

                entry.State = retryDelay is null
                    ? "FailedPaused"
                    : "FailedRetryable";

                entry.NextRetry = retryDelay is null
                    ? null
                    : _now + retryDelay.Value;

                entry.Error = errorCode;
                entry.Token = null;
                entry.LeaseUntil = null;

                return Task.FromResult(true);
            }
        }

        private bool OwnsLease(long id, Guid token)
        {
            return _entries.TryGetValue(id, out var entry)
                && entry.State == "Processing"
                && entry.Token == token
                && entry.LeaseUntil > _now;
        }

        private sealed class Entry
        {
            public required TelegramInboxUpdate Update { get; init; }

            public string State { get; set; } = "Received";
            public int Attempts { get; set; }
            public Guid? Token { get; set; }
            public DateTimeOffset? LeaseUntil { get; set; }
            public DateTimeOffset? NextRetry { get; set; }
            public string? Error { get; set; }
            public string? Result { get; set; }
        }
    }
}
