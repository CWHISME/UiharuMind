using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
// 项目自有的 struct ChatMessage 遮蔽了 Microsoft.Extensions.AI.ChatMessage，阶段 2 会删除它
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 钉死会话持久化的承重假设：历史直接存 <see cref="ChatMessage"/>，
/// 其多态 <see cref="AIContent"/> 必须能无损往返，否则会话重载后上下文会残缺。
/// </summary>
public class SessionJsonOptionsTests
{
    [Fact]
    public void RoundTrip_PreservesAllContentTypes()
    {
        FunctionCallContent call = new("call-1", "run_shell",
            new Dictionary<string, object?> { ["command"] = "ls -la" });

        List<ChatMessage> original =
        [
            new(ChatRole.System, "你是初春。"),
            new(ChatRole.User, [new TextContent("这张图里是什么？"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")])
            {
                AuthorName = "桃子",
                CreatedAt = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero),
            },
            new(ChatRole.Assistant, [new TextReasoningContent("先看图。"), new TextContent("是一只猫。"), call]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "total 0")]),
            new(ChatRole.Assistant, [new ToolApprovalRequestContent("approval-1", call)]),
        ];

        string json = JsonSerializer.Serialize(original, SessionJsonOptions.Default);
        List<ChatMessage> restored =
            JsonSerializer.Deserialize<List<ChatMessage>>(json, SessionJsonOptions.Default)!;

        Assert.Equal(original.Count, restored.Count);

        // 角色与作者名
        Assert.Equal(ChatRole.System, restored[0].Role);
        Assert.Equal("桃子", restored[1].AuthorName);
        Assert.Equal(original[1].CreatedAt, restored[1].CreatedAt);

        // 多模态：文本 + 图片字节
        Assert.Equal("这张图里是什么？", Assert.IsType<TextContent>(restored[1].Contents[0]).Text);
        DataContent restoredData = Assert.IsType<DataContent>(restored[1].Contents[1]);
        Assert.Equal("image/png", restoredData.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, restoredData.Data.ToArray());

        // 思考内容不能退化成普通文本，否则 UI 会把它渲染成正文
        Assert.Equal("先看图。", Assert.IsType<TextReasoningContent>(restored[2].Contents[0]).Text);
        Assert.Equal("是一只猫。", Assert.IsType<TextContent>(restored[2].Contents[1]).Text);

        // 工具调用与结果靠 CallId 配对，丢了就无法还原调用卡片
        FunctionCallContent restoredCall = Assert.IsType<FunctionCallContent>(restored[2].Contents[2]);
        Assert.Equal("call-1", restoredCall.CallId);
        Assert.Equal("run_shell", restoredCall.Name);
        Assert.Equal("ls -la", restoredCall.Arguments!["command"]!.ToString());

        FunctionResultContent restoredResult = Assert.IsType<FunctionResultContent>(restored[3].Contents[0]);
        Assert.Equal("call-1", restoredResult.CallId);
        Assert.Equal("total 0", restoredResult.Result!.ToString());

        // 审批请求必须留住它所对应的工具调用，否则回放时无法还原审批卡片
        ToolApprovalRequestContent restoredApproval =
            Assert.IsType<ToolApprovalRequestContent>(restored[4].Contents[0]);
        Assert.Equal("call-1", restoredApproval.ToolCall.CallId);
        Assert.Equal("run_shell", Assert.IsType<FunctionCallContent>(restoredApproval.ToolCall).Name);
    }

    [Fact]
    public void Serialize_KeepsNonAsciiUnescaped()
    {
        List<ChatMessage> messages = [new(ChatRole.User, "初春です")];

        string json = JsonSerializer.Serialize(messages, SessionJsonOptions.Default);

        Assert.Contains("初春です", json);
        Assert.DoesNotContain("\\u", json);
    }
}
