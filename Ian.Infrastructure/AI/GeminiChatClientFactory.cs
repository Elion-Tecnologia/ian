using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.AI;

// Owns one transport/client per DI container. No conversations or prompts are cached.
public sealed class GeminiChatClientFactory : IDisposable
{
    private readonly HttpClient _transport;
    private readonly Lazy<IChatClient> _client;

    public GeminiChatClientFactory(IOptions<GeminiOptions> options, HttpClient transport)
    {
        _transport = transport;
        _client = new Lazy<IChatClient>(() =>
        {
            var settings = options.Value;
            if (!settings.IsValid())
            {
                throw new OptionsValidationException(Options.DefaultName, typeof(GeminiOptions),
                    ["Configure Gemini:ApiKey, Gemini:Model e Gemini:TimeoutSeconds antes de invocar o modelo."]);
            }

            return new Client(vertexAI: false, apiKey: settings.ApiKey,
                httpOptions: new HttpOptions
                {
                    Timeout = settings.TimeoutSeconds * 1000,
                    RetryOptions = new HttpRetryOptions { Attempts = 1 }
                },
                clientOptions: new ClientOptions { HttpClientFactory = () => _transport })
                .AsIChatClient(settings.Model);
        });
    }

    public IChatClient GetClient() => _client.Value;

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }

        _transport.Dispose();
    }
}
