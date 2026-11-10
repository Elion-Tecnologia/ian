using System.Diagnostics;
using Ian.Application.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.Telegram;

public sealed class TelegramInboxConsumer(
    ITelegramInboxProcessingStore store,
    TelegramUpdateDispatcher dispatcher,
    IOptions<TelegramOptions> telegramOptions,
    IOptions<TelegramInboxProcessingOptions> processingOptions,
    ILogger<TelegramInboxConsumer> logger)
{
    private static readonly ActivitySource ActivitySource =
        new("Ian.Telegram.Inbox");

    public async Task<bool> ProcessNextAsync(
        CancellationToken cancellationToken)
    {
        var options = processingOptions.Value;

        var claimed = await store.TryClaimAsync(
            options.LeaseDuration,
            cancellationToken);

        if (claimed is null)
        {
            return false;
        }

        var updateId = claimed.Update.UpdateId;
        var correlationId = $"tg-{updateId}";

        using var activity = ActivitySource.StartActivity(
            "telegram.inbox.process");

        activity?.SetTag("telegram.update_id", updateId);
        activity?.SetTag("correlation_id", correlationId);

        using var logScope = logger.BeginScope(
            new Dictionary<string, object>
            {
                ["TelegramUpdateId"] = updateId,
                ["CorrelationId"] = correlationId
            });

        using var workCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var heartbeat = HeartbeatAsync(
            claimed,
            workCancellation,
            options);

        try
        {
            if (claimed.Attempt > options.MaxAttempts)
            {
                await store.FailAsync(
                    updateId,
                    claimed.LeaseToken,
                    null,
                    "AttemptsExhausted",
                    cancellationToken);

                logger.LogWarning(
                    "Update {UpdateId} pausado: limite de tentativas.",
                    updateId);

                return true;
            }

            var decision = await dispatcher.DispatchAsync(
                claimed.Update,
                telegramOptions.Value.AdministratorUserId,
                workCancellation.Token);

            workCancellation.Token.ThrowIfCancellationRequested();

            var completed = await store.CompleteAsync(
                updateId,
                claimed.LeaseToken,
                decision.ToString(),
                workCancellation.Token);

            if (!completed)
            {
                logger.LogWarning(
                    "Update {UpdateId} não foi concluído: lease inválido.",
                    updateId);
            }
        }
        catch (TelegramHandlerUnavailableException)
        {
            await store.FailAsync(
                updateId,
                claimed.LeaseToken,
                null,
                "HandlerUnavailable",
                cancellationToken);

            logger.LogInformation(
                "Update {UpdateId} preservado para handler futuro.",
                updateId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Encerramento do processo.
            // O lease expirará e o update poderá ser retomado.
        }
        catch (OperationCanceledException)
            when (workCancellation.IsCancellationRequested)
        {
            logger.LogWarning(
                "Processamento do update {UpdateId} perdeu o lease.",
                updateId);
        }
        catch (Exception)
        {
            // Não registrar exceção ou payload:
            // ambos podem conter informações sensíveis.
            var delay = CalculateRetryDelay(
                claimed.Attempt,
                options);

            var nextRetry = claimed.Attempt >= options.MaxAttempts
                ? (TimeSpan?)null
                : delay;

            await store.FailAsync(
                updateId,
                claimed.LeaseToken,
                nextRetry,
                nextRetry is null
                    ? "AttemptsExhausted"
                    : "ProcessingFailed",
                cancellationToken);

            logger.LogWarning(
                "Falha ao processar update {UpdateId}.",
                updateId);
        }
        finally
        {
            await workCancellation.CancelAsync();
            await heartbeat;
        }

        return true;
    }

    private async Task HeartbeatAsync(
        TelegramClaimedUpdate claimed,
        CancellationTokenSource workCancellation,
        TelegramInboxProcessingOptions options)
    {
        try
        {
            while (!workCancellation.IsCancellationRequested)
            {
                await Task.Delay(
                    options.HeartbeatInterval,
                    workCancellation.Token);

                var renewed = await store.RenewLeaseAsync(
                    claimed.Update.UpdateId,
                    claimed.LeaseToken,
                    options.LeaseDuration,
                    workCancellation.Token);

                if (!renewed)
                {
                    await workCancellation.CancelAsync();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
            when (workCancellation.IsCancellationRequested)
        {
            // Encerramento normal do heartbeat.
        }
        catch (Exception)
        {
            logger.LogWarning(
                "Falha na renovação do lease do update {UpdateId}.",
                claimed.Update.UpdateId);

            await workCancellation.CancelAsync();
        }
    }

    private static TimeSpan CalculateRetryDelay(
        int attempt,
        TelegramInboxProcessingOptions options)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var multiplier = Math.Pow(2, exponent);

        var milliseconds = Math.Min(
            options.RetryBaseDelay.TotalMilliseconds * multiplier,
            options.RetryMaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(milliseconds);
    }
}