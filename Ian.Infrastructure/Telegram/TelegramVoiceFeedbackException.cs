namespace Ian.Infrastructure.Telegram;

public sealed class TelegramVoiceFeedbackException()
    : Exception("Não foi possível confirmar a aceitação da resposta de voz pelo Telegram.");
