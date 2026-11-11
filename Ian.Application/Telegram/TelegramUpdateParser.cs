using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ian.Application.Telegram;

public static class TelegramUpdateParser
{
    public static bool TryParse(
        JsonElement payload,
        out TelegramInboxUpdate? update)
    {
        update = null;

        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("update_id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.Number ||
            !idElement.TryGetInt64(out var updateId) ||
            updateId < 0)
        {
            return false;
        }

        var minimalPayload = new JsonObject
        {
            ["update_id"] = updateId
        };

        if (payload.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.Object)
        {
            var minimalMessage = Project(
                message,
                "message_id",
                "date",
                "text",
                "caption");

            CopyObject(
                message,
                minimalMessage,
                "from",
                "id",
                "is_bot");

            CopyObject(
                message,
                minimalMessage,
                "chat",
                "id",
                "type");

            CopyObject(
                message,
                minimalMessage,
                "document",
                "file_id",
                "mime_type",
                "file_name");

            CopyObject(
                message,
                minimalMessage,
                "left_chat_member",
                "id",
                "is_bot");

            if (message.TryGetProperty(
                    "new_chat_members",
                    out var members) &&
                members.ValueKind == JsonValueKind.Array)
            {
                var minimalMembers = new JsonArray();

                foreach (var member in members.EnumerateArray())
                {
                    if (member.ValueKind == JsonValueKind.Object)
                    {
                        minimalMembers.Add(
                            Project(member, "id", "is_bot"));
                    }
                }

                minimalMessage["new_chat_members"] = minimalMembers;
            }

            minimalPayload["message"] = minimalMessage;
        }

        update = new TelegramInboxUpdate(
            updateId,
            minimalPayload.ToJsonString());

        return true;
    }

    private static void CopyObject(
        JsonElement source,
        JsonObject destination,
        string propertyName,
        params string[] allowedFields)
    {
        if (!source.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        destination[propertyName] = Project(value, allowedFields);
    }

    private static JsonObject Project(
        JsonElement source,
        params string[] allowedFields)
    {
        var result = new JsonObject();

        foreach (var field in allowedFields)
        {
            if (!source.TryGetProperty(field, out var value))
            {
                continue;
            }

            if (value.ValueKind is
                JsonValueKind.String or
                JsonValueKind.Number or
                JsonValueKind.True or
                JsonValueKind.False)
            {
                result[field] =
                    JsonNode.Parse(value.GetRawText());
            }
        }

        return result;
    }
}
