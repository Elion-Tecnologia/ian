using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Infrastructure.Telegram;

public sealed class TelegramInboxBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramInboxProcessingOptions> options,
    ILogger<TelegramInboxBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Processador interno da inbox Telegram iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();

                var consumer = scope.ServiceProvider
                    .GetRequiredService<TelegramInboxConsumer>();

                var processed = await consumer.ProcessNextAsync(
                    stoppingToken);
                var delivered = await scope.ServiceProvider.GetRequiredService<TelegramReplyOutboxConsumer>()
                    .ProcessNextAsync(stoppingToken);

                if (!processed && !delivered)
                {
                    await Task.Delay(
                        options.Value.IdleDelay,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Não registrar detalhes da exceção de banco
                // ou conteúdo da mensagem.
                logger.LogWarning(
                    "Falha no ciclo da inbox Telegram.");

                try
                {
                    await Task.Delay(
                        options.Value.ErrorDelay,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation(
            "Processador interno da inbox Telegram encerrado.");
    }
}
