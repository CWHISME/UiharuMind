using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.Core.Chat;

public sealed class ChatMessageData
{
    public ECharacter Role { get; set; }
    public string AuthorName { get; set; } = "";
    public string Content { get; set; } = "";
    public byte[]? ImageBytes { get; set; }
    public string ImageMediaType { get; set; } = "image/jpeg";
    public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;

    [JsonIgnore]
    public bool HasImage => ImageBytes is { Length: > 0 };

    /// <summary>
    /// 仅在请求模型时转换为 Microsoft.Extensions.AI 消息，持久化格式不依赖具体 AI SDK。
    /// </summary>
    public Microsoft.Extensions.AI.ChatMessage ToAIMessage()
    {
        List<AIContent> contents = [];
        if (HasImage) contents.Add(new DataContent(ImageBytes, ImageMediaType));
        if (!string.IsNullOrEmpty(Content)) contents.Add(new TextContent(Content));

        return new Microsoft.Extensions.AI.ChatMessage(ToChatRole(Role), contents)
        {
            AuthorName = string.IsNullOrWhiteSpace(AuthorName) ? null : AuthorName,
            CreatedAt = new DateTimeOffset(new DateTime(Timestamp, DateTimeKind.Utc))
        };
    }

    private static ChatRole ToChatRole(ECharacter role) => role switch
    {
        ECharacter.System => ChatRole.System,
        ECharacter.Assistant => ChatRole.Assistant,
        ECharacter.Tool => ChatRole.Tool,
        _ => ChatRole.User
    };
}

public readonly struct ChatMessage
{
    public const string NarratorName = "Narrator";

    public required ChatMessageData Message { get; init; }

    public long Timestamp => Message.Timestamp;
    public string CharacterName => string.IsNullOrWhiteSpace(Message.AuthorName) ? "System" : Message.AuthorName;

    public ECharacter Character => Message.Role;

    public string LocalTimeString
    {
        get
        {
            DateTime utcTime = new(Timestamp, DateTimeKind.Utc);
            return utcTime.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
        }
    }
}
