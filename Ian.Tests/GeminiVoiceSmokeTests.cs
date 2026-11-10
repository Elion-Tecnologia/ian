using System.Diagnostics;
using Ian.Api;
using Ian.Application.Telegram;
using Ian.Infrastructure;
using Ian.Infrastructure.AI;
using Ian.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Ian.Tests;

public sealed class GeminiVoiceSmokeTests(ITestOutputHelper output)
{
    [VoiceSmokeTheory]
    [Trait("Category", "GeminiVoiceSmoke")]
    [InlineData("short")]
    [InlineData("silence")]
    [InlineData("unintelligible")]
    [InlineData("fictional")]
    [InlineData("invalid")]
    public async Task SyntheticAudioThroughProductionTranscriber(string scenario)
    {
        // Opt-in is required before accessing fixtures or resolving credentials.
        Assert.True(Environment.GetEnvironmentVariable("IAN_RUN_VOICE_SMOKE") == "1" &&
            Environment.GetEnvironmentVariable("IAN_ALLOW_GEMINI_QUOTA") == "1");
        var directory = Environment.GetEnvironmentVariable("IAN_VOICE_SMOKE_DIRECTORY");
        Assert.False(string.IsNullOrWhiteSpace(directory), "Configure o diretório externo de áudios exclusivamente sintéticos.");
        using var transport = new GeminiSmokeTransport(scenario is "invalid" or "silence" ? 0 : 1,
            new SocketsHttpHandler { AllowAutoRedirect = false });
        // Production adapter/factory/SDK; only the transport gains a test-only dispatch ceiling.
        using var provider = new ServiceCollection().AddInfrastructure(new ConfigurationBuilder()
            .AddUserSecrets(typeof(ApiHost).Assembly, optional: true)
            .AddEnvironmentVariables().Build())
            .AddSingleton(services => new GeminiChatClientFactory(
                services.GetRequiredService<IOptions<GeminiOptions>>(),
                new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan }))
            .BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
        var limits = provider.GetRequiredService<IOptions<TelegramVoiceOptions>>().Value.ToLimits();
        Assert.Equal("gemini-3.1-flash-lite", settings.Model);
        Assert.True(settings.IsValid() && limits.IsValid(), "Configuração de smoke inválida; não enviar.");
        Assert.InRange(settings.TimeoutSeconds, 1, 60);
        Assert.InRange(limits.TimeoutSeconds, 1, 60);
        output.WriteLine("scenario={0}; model={1}; max_bytes={2}; max_seconds={3}; timeout_seconds={4}",
            scenario, settings.Model, limits.MaxBytes, Math.Min(120, limits.MaxDurationSeconds),
            Math.Min(settings.TimeoutSeconds, limits.TimeoutSeconds));
        TelegramVoiceFile? file = null;
        try
        {
            var path = Path.Combine(directory, scenario + ".ogg");
            Assert.True(new FileInfo(path).Length <= Math.Min(limits.MaxBytes, 5 * 1024 * 1024), "Fixture acima do limite do smoke.");
            file = new TelegramVoiceFile(await File.ReadAllBytesAsync(path));
            var elapsed = Stopwatch.StartNew();
            var result = await provider.GetRequiredService<IVoiceTranscriber>().TranscribeAsync(file, 120, default);
            elapsed.Stop();
            output.WriteLine("scenario={0}; model={1}; latency_ms={2}; result={3}; has_text={4}; generation_calls={5}; blocked_requests={6}",
                scenario, settings.Model, elapsed.ElapsedMilliseconds,
                result.Failure?.ToString() ?? "Transcribed", !string.IsNullOrWhiteSpace(result.Text), transport.Calls, transport.Blocked);
            Assert.Equal(0, transport.Blocked);
            switch (scenario)
            {
                case "invalid": Assert.Equal(TelegramVoiceFailure.InvalidFile, result.Failure); break;
                case "silence": Assert.Equal(TelegramVoiceFailure.EmptyTranscription, result.Failure); break;
                case "unintelligible":
                    // Human offline review found the degraded phrase still intelligible.
                    // This is not an oracle requiring the model to refuse correct speech.
                    Assert.True(result.Failure == TelegramVoiceFailure.NeedsClarification ||
                        result.Failure is null && SamePhrase(result.Text, "Palavras sintéticas sem contexto para o ensaio."),
                        "Fala degradada deve preservar a frase sintética ou solicitar esclarecimento.");
                    break;
                case "short":
                    Assert.Null(result.Failure);
                    Assert.True(SamePhrase(result.Text, "Esta é uma mensagem sintética de teste."),
                        "A transcrição não preservou a frase sintética esperada.");
                    break;
                case "fictional":
                    // Safe refusal is acceptable; a fabricated/incorrect identifier is not.
                    Assert.True(result.Failure == TelegramVoiceFailure.NeedsClarification ||
                        result.Failure is null && result.Text?.Contains("Aurora", StringComparison.OrdinalIgnoreCase) == true &&
                        (result.Text.Contains("42") || result.Text.Contains("quarenta e dois", StringComparison.OrdinalIgnoreCase)),
                        "Nome/número sintéticos não preservados nem recusados com esclarecimento.");
                    break;
            }
        }
        catch (Exception)
        {
            Assert.Fail("Smoke de áudio falhou; consulte somente a classificação/métricas seguras. Não repetir sem diagnóstico e autorização.");
        }
        finally
        {
            file?.Dispose();
            output.WriteLine("scenario={0}; dispatched_total={1}; blocked_total={2}", scenario, transport.Calls, transport.Blocked);
        }
    }

    private static bool SamePhrase(string? actual, string expected) => actual is not null &&
        string.Concat(actual.Where(char.IsLetterOrDigit)).Equals(
            string.Concat(expected.Where(char.IsLetterOrDigit)), StringComparison.OrdinalIgnoreCase);
}

public sealed class VoiceSmokeTheoryAttribute : TheoryAttribute
{
    public VoiceSmokeTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("IAN_RUN_VOICE_SMOKE") != "1" ||
            Environment.GetEnvironmentVariable("IAN_ALLOW_GEMINI_QUOTA") != "1")
            Skip = "Smoke de áudio real exige autorização específica da #17, quota e fixtures sintéticas; não executado por padrão.";
    }
}
