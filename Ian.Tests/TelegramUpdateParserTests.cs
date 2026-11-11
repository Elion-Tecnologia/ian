using System.Text.Json;
using Ian.Application.Telegram;

namespace Ian.Tests;

public sealed class TelegramUpdateParserTests
{
    [Fact]
    public void GroupMembershipMetadataIsNotProjected()
    {
        using var source = JsonDocument.Parse("""
            {"update_id":124,"message":{"chat":{"id":-100,"type":"supergroup"},
            "new_chat_members":[{"id":500,"is_bot":true}],
            "left_chat_member":{"id":500,"is_bot":true}}}
            """);
        Assert.True(TelegramUpdateParser.TryParse(source.RootElement, out var update));
        using var stored = JsonDocument.Parse(update!.PayloadJson);
        var message = stored.RootElement.GetProperty("message");
        Assert.False(message.TryGetProperty("new_chat_members", out _));
        Assert.False(message.TryGetProperty("left_chat_member", out _));
        Assert.Equal(TelegramAuthorizationDecision.Denied,
            TelegramUpdateAuthorization.Evaluate(stored.RootElement, 42));
    }

    [Fact]
    public void TextProjectionPreservesIdentityAndTextWithoutMediaMetadata()
    {
        using var source = JsonDocument.Parse("""
            {"update_id":123,"message":{"message_id":17,"date":1,
            "from":{"id":42,"is_bot":false,"first_name":"unused"},
            "chat":{"id":42,"type":"private","username":"unused"},
            "text":"Consultar agenda","voice":{"file_id":"unused","duration":1},
            "audio":{"file_id":"unused","duration":1}}}
            """);

        Assert.True(TelegramUpdateParser.TryParse(source.RootElement, out var update));
        Assert.Equal(123, update!.UpdateId);
        using var stored = JsonDocument.Parse(update.PayloadJson);
        var message = stored.RootElement.GetProperty("message");
        Assert.Equal(123, stored.RootElement.GetProperty("update_id").GetInt64());
        Assert.Equal(17, message.GetProperty("message_id").GetInt64());
        Assert.Equal(42, message.GetProperty("from").GetProperty("id").GetInt64());
        Assert.False(message.GetProperty("from").GetProperty("is_bot").GetBoolean());
        Assert.Equal(42, message.GetProperty("chat").GetProperty("id").GetInt64());
        Assert.Equal("private", message.GetProperty("chat").GetProperty("type").GetString());
        Assert.Equal("Consultar agenda", message.GetProperty("text").GetString());
        Assert.False(message.TryGetProperty("voice", out _));
        Assert.False(message.TryGetProperty("audio", out _));
        Assert.DoesNotContain("unused", update.PayloadJson);
    }
}
