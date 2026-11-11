using System.Text.Json;
using Ian.Application.Telegram;

namespace Ian.Tests;

public sealed class TelegramAuthorizationTests
{
    private const long AdministratorId = 42;

    [Theory]
    [InlineData(
        """{"update_id":1,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"text":"Agendar visita"}}""",
        TelegramAuthorizationDecision.Authorized)]
    [InlineData(
        """{"update_id":2,"message":{"from":{"id":99,"is_bot":false},"chat":{"id":99,"type":"private"},"text":"Cancelar"}}""",
        TelegramAuthorizationDecision.Denied)]
    [InlineData(
        """{"update_id":3,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":-100,"type":"group"},"text":"Cancelar"}}""",
        TelegramAuthorizationDecision.Denied)]
    [InlineData(
        """{"update_id":4,"message":{"from":{"id":42,"is_bot":true},"chat":{"id":42,"type":"private"},"text":"Cancelar"}}""",
        TelegramAuthorizationDecision.Denied)]
    [InlineData(
        """{"update_id":5,"message":{"chat":{"id":42,"type":"private"},"text":"Cancelar"}}""",
        TelegramAuthorizationDecision.Denied)]
    [InlineData(
        """{"update_id":6,"message":{"from":{"id":42},"chat":{"id":42,"type":"private"},"text":"Cancelar"}}""",
        TelegramAuthorizationDecision.Denied)]
    [InlineData(
        """{"update_id":7,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"channel"},"text":"Cancelar"}}""",
        TelegramAuthorizationDecision.Denied)]
    [InlineData(
        """{"update_id":8,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"voice":{"file_id":"audio-123"}}}""",
        TelegramAuthorizationDecision.NonCommandEvent)]
    [InlineData(
        """{"update_id":19,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"audio":{"file_id":"synthetic"},"caption":"Cancelar agendamento"}}""",
        TelegramAuthorizationDecision.NonCommandEvent)]
    [InlineData(
        """{"update_id":9,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":-100,"type":"supergroup"},"new_chat_members":[{"id":500,"is_bot":true}]}}""",
        TelegramAuthorizationDecision.Denied)]
    [InlineData(
        """{"update_id":10,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":-100,"type":"supergroup"},"new_chat_members":[{"id":500,"is_bot":false}]}}""",
        TelegramAuthorizationDecision.Denied)]
    public void EvaluatesAuthorizationCorrectly(
        string json,
        TelegramAuthorizationDecision expected)
    {
        using var document = JsonDocument.Parse(json);

        var decision = TelegramUpdateAuthorization.Evaluate(
            document.RootElement,
            AdministratorId);

        Assert.Equal(expected, decision);
    }

    [Fact]
    public void DeniesWhenAdministratorIdDoesNotMatch()
    {
        const string json =
            """{"update_id":11,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"text":"Teste"}}""";

        using var document = JsonDocument.Parse(json);

        var decision = TelegramUpdateAuthorization.Evaluate(
            document.RootElement,
            administratorUserId: 99);

        Assert.Equal(TelegramAuthorizationDecision.Denied, decision);
    }

    [Fact]
    public async Task AuthorizedMessageReachesAdministrativeHandler()
    {
        var administrativeHandler = new AdministrativeHandlerSpy();

        var dispatcher = new TelegramUpdateDispatcher(
            administrativeHandler);

        var update = new TelegramInboxUpdate(
            12,
            """{"update_id":12,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":42,"type":"private"},"text":"Agendar visita"}}""");

        var decision = await dispatcher.DispatchAsync(
            update,
            AdministratorId);

        Assert.Equal(TelegramAuthorizationDecision.Authorized, decision);
        Assert.Equal(1, administrativeHandler.Calls);
    }

    [Theory]
    [InlineData(
        """{"update_id":13,"message":{"from":{"id":99,"is_bot":false},"chat":{"id":99,"type":"private"},"text":"Comando proibido"}}""")]
    [InlineData(
        """{"update_id":14,"message":{"from":{"id":42,"is_bot":false},"chat":{"id":-100,"type":"group"},"text":"Comando proibido"}}""")]
    [InlineData(
        """{"update_id":15,"message":{"from":{"id":42,"is_bot":true},"chat":{"id":42,"type":"private"},"text":"Comando proibido"}}""")]
    [InlineData(
        """{"update_id":16,"message":{"chat":{"id":42,"type":"private"},"text":"Comando proibido"}}""")]
    public async Task DeniedMessagesNeverReachHandlers(string json)
    {
        var administrativeHandler = new AdministrativeHandlerSpy();

        var dispatcher = new TelegramUpdateDispatcher(
            administrativeHandler);

        using var document = JsonDocument.Parse(json);

        var updateId = document.RootElement
            .GetProperty("update_id")
            .GetInt64();

        var decision = await dispatcher.DispatchAsync(
            new TelegramInboxUpdate(updateId, json),
            AdministratorId);

        Assert.Equal(TelegramAuthorizationDecision.Denied, decision);
        Assert.Equal(0, administrativeHandler.Calls);
    }

    [Fact]
    public async Task GroupEventNeverExecutesAdministrativeHandler()
    {
        var administrativeHandler = new AdministrativeHandlerSpy();

        var dispatcher = new TelegramUpdateDispatcher(
            administrativeHandler);

        var update = new TelegramInboxUpdate(
            17,
            """{"update_id":17,"message":{"from":{"id":99,"is_bot":false},"chat":{"id":-100123,"type":"supergroup"},"new_chat_members":[{"id":500,"is_bot":true}]}}""");

        var decision = await dispatcher.DispatchAsync(
            update,
            AdministratorId);

        Assert.Equal(
            TelegramAuthorizationDecision.Denied,
            decision);

        Assert.Equal(0, administrativeHandler.Calls);
    }

    [Fact]
    public async Task InvalidStoredJsonDoesNotReachHandlers()
    {
        var administrativeHandler = new AdministrativeHandlerSpy();

        var dispatcher = new TelegramUpdateDispatcher(
            administrativeHandler);

        var decision = await dispatcher.DispatchAsync(
            new TelegramInboxUpdate(18, "{invalid-json"),
            AdministratorId);

        Assert.Equal(TelegramAuthorizationDecision.Denied, decision);
        Assert.Equal(0, administrativeHandler.Calls);
    }

    private sealed class AdministrativeHandlerSpy
        : ITelegramAuthorizedUpdateHandler
    {
        public int Calls { get; private set; }

        public Task HandleAsync(
            TelegramInboxUpdate update,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
