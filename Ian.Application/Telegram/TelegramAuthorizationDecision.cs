namespace Ian.Application.Telegram;

public enum TelegramAuthorizationDecision
{
    Authorized,
    Denied,
    NonCommandEvent
}

public sealed record TelegramGroupEventCandidate(
    long UpdateId,
    long ChatId);