using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ian.Application.Telegram;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.Telegram;

// Owns the HttpClient. The production transport has no request logging handlers:
// Telegram places the bot token in its URL, including in file download URLs.
public sealed class TelegramVoiceFileClient(
    HttpClient transport,
    IOptions<TelegramOptions> telegramOptions,
    IOptions<TelegramVoiceOptions> voiceOptions) :
    ITelegramVoiceFileClient, ITelegramVoiceFeedback, IDisposable
{
    private const int MaxMetadataBytes = 32 * 1024;

    public async Task<TelegramVoiceDownloadResult> DownloadAsync(
        string fileId,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId) ||
            maxBytes is <= 0 or > 20_000_000)
        {
            return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.InvalidFile);
        }

        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BotMethodUri("getFile"))
            {
                Content = JsonContent.Create(new { file_id = fileId })
            };
            using var response = await transport.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return TelegramVoiceDownloadResult.Rejected(ClassifyStatus(response.StatusCode));
            }

            var metadata = await ReadBoundedAsync(response.Content, MaxMetadataBytes, timeout.Token);
            string? filePath;
            try
            {
                using var document = JsonDocument.Parse(metadata);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("result", out var result) ||
                    result.ValueKind != JsonValueKind.Object ||
                    !result.TryGetProperty("file_path", out var path) ||
                    path.ValueKind != JsonValueKind.String)
                {
                    return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.InvalidFile);
                }

                filePath = path.GetString();
                if (!IsSafeFilePath(filePath))
                {
                    return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.InvalidFile);
                }

                if (result.TryGetProperty("file_size", out var size))
                {
                    if (size.ValueKind != JsonValueKind.Number ||
                        !size.TryGetInt64(out var declaredSize) || declaredSize <= 0)
                    {
                        return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.InvalidFile);
                    }

                    if (declaredSize > maxBytes)
                    {
                        return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.SizeLimitExceeded);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(metadata);
            }

            using var download = new HttpRequestMessage(HttpMethod.Get,
                new Uri($"https://api.telegram.org/file/bot{EscapedToken}/{filePath}"));
            using var fileResponse = await transport.SendAsync(
                download, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (!fileResponse.IsSuccessStatusCode)
            {
                return TelegramVoiceDownloadResult.Rejected(ClassifyStatus(fileResponse.StatusCode));
            }

            var bytes = await ReadBoundedAsync(fileResponse.Content, maxBytes, timeout.Token);
            if (bytes.Length == 0)
            {
                return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.InvalidFile);
            }

            return TelegramVoiceDownloadResult.Downloaded(new TelegramVoiceFile(bytes));
        }
        catch (VoiceSizeLimitException)
        {
            return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.SizeLimitExceeded);
        }
        catch (JsonException)
        {
            return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.InvalidFile);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.DownloadUnavailable);
        }
        catch (IOException)
        {
            return TelegramVoiceDownloadResult.Rejected(TelegramVoiceFailure.DownloadUnavailable);
        }
    }

    public async Task SendAsync(
        long chatId,
        TelegramVoiceFailure failure,
        CancellationToken cancellationToken)
    {
        if (chatId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chatId));
        }

        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BotMethodUri("sendMessage"))
            {
                Content = JsonContent.Create(new
                {
                    chat_id = chatId,
                    text = FeedbackText(failure)
                })
            };
            using var response = await transport.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new TelegramVoiceFeedbackException();
            }

            var bytes = await ReadBoundedAsync(response.Content, MaxMetadataBytes, timeout.Token);
            try
            {
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("ok", out var ok) ||
                    ok.ValueKind != JsonValueKind.True)
                {
                    throw new TelegramVoiceFeedbackException();
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or
                OperationCanceledException or VoiceSizeLimitException)
        {
            // Do not propagate the original exception: it may contain the token URL.
            throw new TelegramVoiceFeedbackException();
        }
    }

    public void Dispose() => transport.Dispose();

    private string EscapedToken => Uri.EscapeDataString(telegramOptions.Value.BotToken);

    private Uri BotMethodUri(string method) =>
        new($"https://api.telegram.org/bot{EscapedToken}/{method}");

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(voiceOptions.Value.TimeoutSeconds));
        return timeout;
    }

    private static TelegramVoiceFailure ClassifyStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.NotFound => TelegramVoiceFailure.InvalidFile,
        HttpStatusCode.TooManyRequests => TelegramVoiceFailure.RateLimited,
        _ => TelegramVoiceFailure.DownloadUnavailable
    };

    private static bool IsSafeFilePath(string? path) =>
        !string.IsNullOrEmpty(path) && path.Length <= 1024 &&
        path.Split('/').All(segment =>
            segment.Length > 0 && segment is not ("." or "..") &&
            segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'));

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw new VoiceSizeLimitException();
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes + 1];
        try
        {
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0)
                {
                    return buffer.AsSpan(0, length).ToArray();
                }

                length += read;
                if (length > maxBytes)
                {
                    throw new VoiceSizeLimitException();
                }
            }

            throw new VoiceSizeLimitException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static string FeedbackText(TelegramVoiceFailure failure) => failure switch
    {
        TelegramVoiceFailure.SizeLimitExceeded =>
            "O áudio excede o limite de tamanho. Envie um áudio menor ou escreva o pedido.",
        TelegramVoiceFailure.DurationLimitExceeded =>
            "O áudio excede o limite de duração. Envie um áudio mais curto ou escreva o pedido.",
        TelegramVoiceFailure.EmptyTranscription or TelegramVoiceFailure.NeedsClarification =>
            "Não consegui entender o pedido com segurança. Pode esclarecer por texto ou enviar outro áudio?",
        TelegramVoiceFailure.InvalidFile =>
            "Não consegui ler esse arquivo de voz. Envie outro áudio ou escreva o pedido.",
        _ => "Não consegui processar o áudio agora. Tente novamente mais tarde ou escreva o pedido."
    };

    private sealed class VoiceSizeLimitException : Exception;
}
