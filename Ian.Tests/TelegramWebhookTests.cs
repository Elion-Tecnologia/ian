
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Ian.Application.Telegram;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ian.Tests;

public sealed class TelegramWebhookTests
{
    private const string Route = "/webhooks/telegram";
    private const string Secret = "Test_secret-123";

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret")]
    public async Task RejectsUnauthorizedRequests(string? secret)
    {
        var writer = new FakeWriter();

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        using var response = await PostAsync(
            client,
            """{"update_id":100}""",
            secret);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task RejectsInvalidJson()
    {
        var writer = new FakeWriter();

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        using var response = await PostAsync(
            client,
            "{\"update_id\":",
            Secret);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task RejectsMissingUpdateId()
    {
        var writer = new FakeWriter();

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        using var response = await PostAsync(
            client,
            """{"message":{"text":"teste"}}""",
            Secret);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(writer.Items);
    }

    [Fact]
    public async Task StoresValidUpdate()
    {
        var writer = new FakeWriter();

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        using var response = await PostAsync(
            client,
            """{"update_id":101,"message":{"text":"teste"}}""",
            Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(writer.Items.ContainsKey(101));

        var stored = writer.Items[101];

        Assert.Equal(101, stored.UpdateId);
        Assert.Contains("teste", stored.PayloadJson);
    }

    [Fact]
    public async Task AcceptsUnknownUpdateType()
    {
        var writer = new FakeWriter();

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        using var response = await PostAsync(
            client,
            """{"update_id":102,"future_event":{"value":1}}""",
            Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = writer.Items[102];

        Assert.Equal(
            """{"update_id":102}""",
            stored.PayloadJson);
    }

    [Fact]
    public async Task Returns503WhenWriterFails()
    {
        var writer = new FakeWriter
        {
            Fail = true
        };

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        using var response = await PostAsync(
            client,
            """{"update_id":103}""",
            Secret);

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);

        Assert.Empty(writer.Items);
    }

    [Fact]
    public async Task DoesNotRespondBeforeStorageCompletes()
    {
        var writer = new FakeWriter
        {
            CommitGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        var requestTask = PostAsync(
            client,
            """{"update_id":104}""",
            Secret);

        try
        {
            await writer.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            Assert.False(requestTask.IsCompleted);
            Assert.Empty(writer.Items);
        }
        finally
        {
            writer.CommitGate.SetResult(true);
        }

        using var response = await requestTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(writer.Items.ContainsKey(104));
    }

    [Fact]
    public async Task RejectsOversizedPayload()
    {
        var writer = new FakeWriter();

        using var factory = CreateFactory(writer);
        using var client = CreateClient(factory);

        var payload = """ {"update_id":105,"data":" """ +
                      new string('x', 300_000) + "\"}";

        using var response = await PostAsync(
            client,
            payload,
            Secret);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);

        Assert.Empty(writer.Items);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string payload,
        string? secret)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            Route);

        request.Content = new StringContent(
            payload,
            Encoding.UTF8,
            "application/json");

        if (secret is not null)
        {
            request.Headers.Add(
                "X-Telegram-Bot-Api-Secret-Token",
                secret);
        }

        return await client.SendAsync(request);
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeWriter writer)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Telegram:BotToken"] = "test-bot-token",
                            ["Telegram:AdministratorUserId"] = "42",
                            ["Telegram:WebhookSecret"] = Secret,
                            ["ConnectionStrings:Default"] =
                                "Host=localhost;Database=ian_test;Username=test;Password=test"
                        });
                });

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITelegramUpdateInboxWriter>();

                    services.AddSingleton<
                        ITelegramUpdateInboxWriter>(writer);
                });
            });
    }

    private sealed class FakeWriter : ITelegramUpdateInboxWriter
    {
        private int _calls;

        public ConcurrentDictionary<long, TelegramInboxUpdate>
            Items { get; } = new();

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool>? CommitGate { get; set; }

        public bool Fail { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public async Task StoreAsync(
            TelegramInboxUpdate update,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            Started.TrySetResult(true);

            if (CommitGate is not null)
            {
                await CommitGate.Task.WaitAsync(cancellationToken);
            }

            if (Fail)
            {
                throw new InvalidOperationException(
                    "Falha simulada na persistência.");
            }

            Items.TryAdd(update.UpdateId, update);
        }
    }
}
