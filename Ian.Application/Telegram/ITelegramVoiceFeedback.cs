namespace Ian.Application.Telegram;

public interface ITelegramVoiceFeedback
{
    Task SendAsync(
        long chatId,
        TelegramVoiceFailure failure,
        CancellationToken cancellationToken);
}
