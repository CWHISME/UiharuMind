using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话存档必须是一个对象。曾因 ChatSession 实现了 IEnumerable&lt;ChatMessage&gt;，
/// System.Text.Json 把它序列化成了裸消息数组——SessionId / Title / CharacterId /
/// CustomParams / WorkspacePath 等全部丢失，且反序列化不回来。
/// </summary>
public class ChatSessionSerializationTests
{
    [Fact]
    public void Session_SerializesAsObjectNotArray()
    {
        ChatSession session = new()
        {
            SessionId = "abc123",
            Title = "初春",
            Description = "开场白",
            CharacterId = "UiharuKazari",
            MemoryName = "notes",
            WorkspacePath = "/tmp/work",
            PermissionModeIndex = 2,
            CustomParams = { ["lang"] = "中文" },
            History = { new ChatMessage(ChatRole.User, "你好") },
        };

        string json = JsonSerializer.Serialize(session, SessionJsonOptions.Default);

        Assert.StartsWith("{", json.TrimStart());
        // 历史不随会话头序列化(单独走 JSONL),混进来意味着每次头保存都回到全量成本
        Assert.DoesNotContain("你好", json);

        ChatSession restored = JsonSerializer.Deserialize<ChatSession>(json, SessionJsonOptions.Default)!;

        Assert.Equal("abc123", restored.SessionId);
        Assert.Equal("初春", restored.Title);
        Assert.Equal("开场白", restored.Description);
        Assert.Equal("UiharuKazari", restored.CharacterId);
        Assert.Equal("notes", restored.MemoryName);
        Assert.Equal("/tmp/work", restored.WorkspacePath);
        Assert.Equal(2, restored.PermissionModeIndex);
        Assert.Equal("中文", restored.CustomParams["lang"]!.ToString());
        Assert.Empty(restored.History);
    }

    [Fact]
    public void Meta_ProjectionKeepsEverythingTheListNeeds()
    {
        ChatSession session = new()
        {
            SessionId = "s1",
            Title = "标题",
            Description = "副标题",
            CharacterId = "Vision",
            History = { new ChatMessage(ChatRole.User, "a"), new ChatMessage(ChatRole.Assistant, "b") },
        };

        ChatSessionMeta meta = session.ToMeta();

        Assert.Equal("s1", meta.SessionId);
        Assert.Equal("标题", meta.Title);
        Assert.Equal("副标题", meta.Description);
        Assert.Equal("Vision", meta.CharacterId);
        Assert.Equal(2, meta.MessageCount);
    }
}
