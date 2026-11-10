using System.Text.Json;

namespace Ian.Application.Telegram;

public static class TelegramUpdateAuthorization
{
    public static TelegramAuthorizationDecision Evaluate(
        JsonElement update,
        long administratorUserId)
    {
        if (administratorUserId <= 0 ||
            update.ValueKind != JsonValueKind.Object ||
            !update.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object)
        {
            return TelegramAuthorizationDecision.Denied;
        }

        if (!message.TryGetProperty("chat", out var chat) ||
            chat.ValueKind != JsonValueKind.Object ||
            !TryGetString(chat, "type", out var chatType))
        {
            return TelegramAuthorizationDecision.Denied;
        }

        // Eventos de associação de grupos não são comandos administrativos.
        if (chatType is "group" or "supergroup")
        {
            return TryGetGroupCandidate(update, out _)
                ? TelegramAuthorizationDecision.NonCommandEvent
                : TelegramAuthorizationDecision.Denied;
        }

        // Somente conversas privadas podem executar comandos.
        if (chatType != "private")
        {
            return TelegramAuthorizationDecision.Denied;
        }

        if (!message.TryGetProperty("from", out var sender) ||
            sender.ValueKind != JsonValueKind.Object ||
            !TryGetInt64(sender, "id", out var senderId) ||
            !TryGetBoolean(sender, "is_bot", out var isBot) ||
            isBot ||
            senderId != administratorUserId)
        {
            return TelegramAuthorizationDecision.Denied;
        }

        return TelegramAuthorizationDecision.Authorized;
    }

    public static bool TryGetGroupCandidate(
        JsonElement update,
        out TelegramGroupEventCandidate? candidate)
    {
        candidate = null;

        if (update.ValueKind != JsonValueKind.Object ||
            !update.TryGetProperty("update_id", out var updateIdElement) ||
            updateIdElement.ValueKind != JsonValueKind.Number ||
            !updateIdElement.TryGetInt64(out var updateId) ||
            updateId < 0 ||
            !update.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("chat", out var chat) ||
            chat.ValueKind != JsonValueKind.Object ||
            !TryGetString(chat, "type", out var chatType) ||
            chatType is not ("group" or "supergroup") ||
            !TryGetInt64(chat, "id", out var chatId) ||
            !message.TryGetProperty("new_chat_members", out var members) ||
            members.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind == JsonValueKind.Object &&
                TryGetBoolean(member, "is_bot", out var isBot) &&
                isBot)
            {
                candidate = new TelegramGroupEventCandidate(
                    updateId,
                    chatId);

                return true;
            }
        }

        return false;
    }

    private static bool TryGetInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = default;

        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null;
    }

    private static bool TryGetBoolean(
        JsonElement element,
        string propertyName,
        out bool value)
    {
        value = default;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }
}