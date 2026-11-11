using System.Net.Http.Json;
using System.Text.Json;
using Ian.Application.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.Telegram;

/// <summary>Owns its transport. No HttpClientFactory logging handlers: Telegram embeds the bot token in the URI.</summary>
public sealed class TelegramTextClient(
    HttpClient transport,
    IOptions<TelegramOptions> telegram,
    IOptions<TelegramTextSendingOptions> sending,
    TimeProvider clock,
    ILogger<TelegramTextClient> logger) : ITelegramTextSender, IDisposable
{
    private const int MaxTextLength = 4096;
    private const int MaxResponseBytes = 65_536;

    public async Task<TelegramTextSendResult> SendAsync(
        TelegramInboxUpdate source, string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = telegram.Value;
        var limits = sending.Value;
        if (!IsAuthorized(source, settings.AdministratorUserId))
            return new(TelegramTextSendStatus.UnauthorizedRecipient, 0);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength || text.Contains('\0'))
            return new(TelegramTextSendStatus.InvalidMessage, 0);
        if (!limits.IsValid() || !IsSafeToken(settings.BotToken))
            return new(TelegramTextSendStatus.InvalidConfiguration, 0);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(limits.TotalTimeoutSeconds), clock);
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var started = clock.GetTimestamp();
        var attempts = 0;
        var uncertain = false;
        try
        {
            while (true)
            {
                overall.Token.ThrowIfCancellationRequested();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(limits.RequestTimeoutSeconds), clock);
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(overall.Token, timeout.Token);
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    new Uri($"https://api.telegram.org/bot{settings.BotToken}/sendMessage"))
                {
                    Content = JsonContent.Create(new
                    {
                        chat_id = settings.AdministratorUserId,
                        text,
                        link_preview_options = new { is_disabled = true }
                    })
                };
                attempts++;
                uncertain = true;
                TelegramTextSendResult result;
                using (var response = await transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token))
                {
                    result = await ClassifyAsync(response, settings.AdministratorUserId, attempts, attempt.Token);
                }
                attempt.Token.ThrowIfCancellationRequested();
                uncertain = result.DeliveryUncertain;
                Log(source.UpdateId, result);

                // Only a definite 429 rejection authorizes another attempt. sendMessage has no idempotency key.
                if (result.Status != TelegramTextSendStatus.RateLimited || result.RetryAfter is not { } delay ||
                    attempts >= limits.MaxAttempts || delay > TimeSpan.FromSeconds(limits.MaxRetryDelaySeconds) ||
                    delay >= TimeSpan.FromSeconds(limits.TotalTimeoutSeconds) - clock.GetElapsedTime(started))
                    return result;
                await Task.Delay(delay, clock, overall.Token);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Never propagate provider exceptions containing the bot URI, text or response body.
            logger.LogInformation("Telegram text reply for update {UpdateId}: cancelled, attempt {Attempt}.", source.UpdateId, attempts);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Failure(source.UpdateId, TelegramTextSendStatus.Timeout, attempts, uncertain);
        }
        catch (Exception) when (deadline.IsCancellationRequested)
        {
            return Failure(source.UpdateId, TelegramTextSendStatus.Timeout, attempts, uncertain);
        }
        catch (HttpRequestException)
        {
            return Failure(source.UpdateId, TelegramTextSendStatus.TransportFailure, attempts, uncertain);
        }
        catch (IOException)
        {
            return Failure(source.UpdateId, TelegramTextSendStatus.TransportFailure, attempts, uncertain);
        }
        catch (JsonException)
        {
            return Failure(source.UpdateId, TelegramTextSendStatus.InvalidResponse, attempts, uncertain);
        }
        catch (Exception)
        {
            return Failure(source.UpdateId, TelegramTextSendStatus.UnexpectedFailure, attempts, uncertain);
        }
    }

    private static bool IsAuthorized(TelegramInboxUpdate source, long administratorId)
    {
        if (source is null || source.UpdateId < 0 || string.IsNullOrWhiteSpace(source.PayloadJson)) return false;
        try
        {
            using var json = JsonDocument.Parse(source.PayloadJson);
            return TelegramUpdateAuthorization.Evaluate(json.RootElement, administratorId) == TelegramAuthorizationDecision.Authorized &&
                json.RootElement.GetProperty("message").GetProperty("chat").TryGetProperty("id", out var chatId) &&
                chatId.TryGetInt64(out var id) && id == administratorId;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool IsSafeToken(string token) => token.Length is > 0 and <= 256 &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '_' or '-');

    private static async Task<TelegramTextSendResult> ClassifyAsync(
        HttpResponseMessage response, long recipientId, int attempts, CancellationToken token)
    {
        var status = (int)response.StatusCode;
        if (status is >= 500 or 401 or 408 || (!response.IsSuccessStatusCode && status != 429))
            return Rejected(status, attempts);

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (body.Length + read > MaxResponseBytes)
                return new(TelegramTextSendStatus.InvalidResponse, attempts, DeliveryUncertain: status != 429);
            body.Write(buffer, 0, read);
        }
        JsonDocument document;
        try { document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length)); }
        catch (JsonException)
        {
            return new(status == 429 ? TelegramTextSendStatus.RateLimited : TelegramTextSendStatus.InvalidResponse,
                attempts, DeliveryUncertain: status != 429);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ok", out var ok))
            {
                if (ok.ValueKind == JsonValueKind.False && root.TryGetProperty("error_code", out var code) &&
                    code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var error))
                {
                    if (error == 429 && status is 429 or >= 200 and < 300)
                    {
                        TimeSpan? delay = null;
                        if (root.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object &&
                            parameters.TryGetProperty("retry_after", out var retry) && retry.ValueKind == JsonValueKind.Number &&
                            retry.TryGetInt32(out var seconds) && seconds > 0)
                            delay = TimeSpan.FromSeconds(seconds);
                        return new(TelegramTextSendStatus.RateLimited, attempts, RetryAfter: delay);
                    }
                    return Rejected(error, attempts);
                }
                if (response.IsSuccessStatusCode && ok.ValueKind == JsonValueKind.True && root.TryGetProperty("result", out var result) &&
                    result.ValueKind == JsonValueKind.Object && result.TryGetProperty("message_id", out var messageId) &&
                    messageId.ValueKind == JsonValueKind.Number && messageId.TryGetInt64(out var id) && id > 0 && result.TryGetProperty("chat", out var chat) &&
                    chat.ValueKind == JsonValueKind.Object && chat.TryGetProperty("id", out var chatId) &&
                    chatId.ValueKind == JsonValueKind.Number && chatId.TryGetInt64(out var returnedId) && returnedId == recipientId &&
                    chat.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "private")
                    return new(TelegramTextSendStatus.Accepted, attempts, MessageId: id);
            }
            return new(status == 429 ? TelegramTextSendStatus.RateLimited : TelegramTextSendStatus.InvalidResponse,
                attempts, DeliveryUncertain: status != 429);
        }
    }

    private static TelegramTextSendResult Rejected(int status, int attempts) => status switch
    {
        401 => new(TelegramTextSendStatus.AuthenticationFailure, attempts),
        408 => new(TelegramTextSendStatus.Timeout, attempts, DeliveryUncertain: true),
        >= 500 => new(TelegramTextSendStatus.ServerFailure, attempts, DeliveryUncertain: true),
        _ => new(TelegramTextSendStatus.Rejected, attempts)
    };

    private TelegramTextSendResult Failure(long updateId, TelegramTextSendStatus status, int attempts, bool uncertain)
    {
        var result = new TelegramTextSendResult(status, attempts, DeliveryUncertain: uncertain);
        Log(updateId, result);
        return result;
    }

    private void Log(long updateId, TelegramTextSendResult result) =>
        logger.LogInformation("Telegram text reply for update {UpdateId}: {Status}, attempt {Attempt}.", updateId, result.Status, result.Attempts);

    public void Dispose() => transport.Dispose();
}
