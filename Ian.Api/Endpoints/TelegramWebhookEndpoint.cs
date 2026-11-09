
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ian.Application.Telegram;
using Ian.Infrastructure.Telegram;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Ian.Api.Endpoints;

public static class TelegramWebhookEndpoint
{
    private const string SecretHeader =
        "X-Telegram-Bot-Api-Secret-Token";

    private const int MaxPayloadBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapTelegramWebhook(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/webhooks/telegram",
            ReceiveAsync);

        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext context,
        IOptions<TelegramOptions> options,
        ITelegramUpdateInboxWriter writer,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(
            "Ian.Api.TelegramWebhook");

        // Primeiro autentica, antes de ler ou persistir o payload.
        if (!context.Request.Headers.TryGetValue(
                SecretHeader, out var header) ||
            header.Count != 1 ||
            !ValidateSecret(
                header.ToString(),
                options.Value.WebhookSecret))
        {
            return Results.Unauthorized();
        }

        if (!context.Request.HasJsonContentType())
        {
            return Results.StatusCode(
                StatusCodes.Status415UnsupportedMediaType);
        }

        if (context.Request.ContentLength > MaxPayloadBytes)
        {
            return Results.StatusCode(
                StatusCodes.Status413PayloadTooLarge);
        }

        var maxBodyFeature = context.Features
            .Get<IHttpMaxRequestBodySizeFeature>();

        if (maxBodyFeature is { IsReadOnly: false })
        {
            maxBodyFeature.MaxRequestBodySize = MaxPayloadBytes;
        }

        byte[] payload;

        try
        {
            using var stream = new MemoryStream();
            var buffer = new byte[8192];

            while (true)
            {
                var bytesRead = await context.Request.Body.ReadAsync(
                    buffer.AsMemory(),
                    context.RequestAborted);

                if (bytesRead == 0)
                    break;

                if (stream.Length + bytesRead > MaxPayloadBytes)
                {
                    return Results.StatusCode(
                        StatusCodes.Status413PayloadTooLarge);
                }

                await stream.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    context.RequestAborted);
            }

            payload = stream.ToArray();
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode ==
                  StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(
                StatusCodes.Status413PayloadTooLarge);
        }

        TelegramInboxUpdate? update;

        try
        {
            using var document = JsonDocument.Parse(payload);

            if (!TelegramUpdateParser.TryParse(
                    document.RootElement,
                    out update) ||
                update is null)
            {
                return Results.BadRequest();
            }
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        try
        {
            await writer.StoreAsync(
                update,
                context.RequestAborted);

            return Results.Ok();
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError(
                "Falha ao armazenar update Telegram {UpdateId}.",
                update.UpdateId);

            return Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool ValidateSecret(
        string provided,
        string expected)
    {
        if (string.IsNullOrEmpty(expected))
            return false;

        var providedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(provided));

        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(
            providedHash,
            expectedHash);
    }
}
