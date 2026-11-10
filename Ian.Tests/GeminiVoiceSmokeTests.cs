using System.Diagnostics;
using Ian.Api;
using Ian.Application.Telegram;
using Ian.Infrastructure;
using Ian.Infrastructure.AI;
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
        using var provider = new ServiceCollection().AddInfrastructure(new ConfigurationBuilder()
            .AddUserSecrets(typeof(ApiHost).Assembly, optional: true)
            .AddEnvironmentVariables().Build()).BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
        Assert.Equal("gemini-3.1-flash-lite", settings.Model);
        TelegramVoiceFile? file = null;
        try
        {
            var path = Path.Combine(directory, scenario + ".ogg");
            Assert.True(new FileInfo(path).Length <= 5 * 1024 * 1024, "Fixture acima do limite do smoke.");
            file = new TelegramVoiceFile(await File.ReadAllBytesAsync(path));
            var elapsed = Stopwatch.StartNew();
            var result = await provider.GetRequiredService<IVoiceTranscriber>().TranscribeAsync(file, 120, default);
            elapsed.Stop();
            output.WriteLine("scenario={0}; model={1}; latency_ms={2}; result={3}; has_text={4}",
                scenario, settings.Model, elapsed.ElapsedMilliseconds,
                result.Failure?.ToString() ?? "Transcribed", !string.IsNullOrWhiteSpace(result.Text));
            switch (scenario)
            {
                case "invalid": Assert.Equal(TelegramVoiceFailure.InvalidFile, result.Failure); break;
                case "silence": Assert.Equal(TelegramVoiceFailure.EmptyTranscription, result.Failure); break;
                case "unintelligible": Assert.Equal(TelegramVoiceFailure.NeedsClarification, result.Failure); break;
                case "short":
                    Assert.Null(result.Failure);
                    Assert.True(result.Text?.Contains("mensagem sintética", StringComparison.OrdinalIgnoreCase) == true,
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
        finally { file?.Dispose(); }
    }
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
