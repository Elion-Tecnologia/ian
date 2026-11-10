namespace Ian.Application.Telegram;

public interface ITelegramVoiceFileClient
{
    Task<TelegramVoiceDownloadResult> DownloadAsync(
        string fileId,
        int maxBytes,
        CancellationToken cancellationToken);
}

public sealed record TelegramVoiceDownloadResult
{
    private TelegramVoiceDownloadResult(
        TelegramVoiceFile? file,
        TelegramVoiceFailure? failure)
    {
        File = file;
        Failure = failure;
    }

    public TelegramVoiceFile? File { get; }
    public TelegramVoiceFailure? Failure { get; }

    public static TelegramVoiceDownloadResult Downloaded(TelegramVoiceFile file) =>
        new(file, null);

    public static TelegramVoiceDownloadResult Rejected(TelegramVoiceFailure failure) =>
        new(null, failure);
}
