using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 钉死思考期「空正文 + 思考碎片」的规整:
/// ① 空正文/空思考被丢掉(它们会切碎思考段,并在请求体里堆成一墙空 text);
/// ② 相邻思考碎片并回一段(否则按 tool_call 回填只能取到最后一个碎片);
/// ③ 干净的消息原样不动。
/// </summary>
public class ChatContentNormalizerTests
{
    [Fact]
    public void EmptyTextBetweenReasoningFragmentsIsDroppedAndReasoningMerged()
    {
        // 服务端思考期每个 chunk 给一段 reasoning_content 加一个空 content
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextContent(""),
            new TextReasoningContent("用户"),
            new TextContent(""),
            new TextReasoningContent("报告了一个 bug"),
            new TextContent(""),
            new TextContent("我先查一下"),
            new FunctionCallContent("call_1", "file_memory_ls"),
        ]);

        Assert.True(ChatContentNormalizer.Normalize(message));

        Assert.Collection(message.Contents,
            x => Assert.Equal("用户报告了一个 bug", Assert.IsType<TextReasoningContent>(x).Text),
            x => Assert.Equal("我先查一下", Assert.IsType<TextContent>(x).Text),
            x => Assert.Equal("call_1", Assert.IsType<FunctionCallContent>(x).CallId));
    }

    [Fact]
    public void ReasoningSeparatedByRealTextIsNotMerged()
    {
        ChatMessage message = new(ChatRole.Assistant,
        [
            new TextReasoningContent("先想"),
            new TextContent("说一句"),
            new TextReasoningContent("再想"),
        ]);

        Assert.False(ChatContentNormalizer.Normalize(message));
        Assert.Equal(3, message.Contents.Count);
    }

    [Fact]
    public void CleanMessageIsUntouched()
    {
        ChatMessage message = new(ChatRole.Assistant, "普通回复");
        IList<AIContent> before = message.Contents;

        Assert.False(ChatContentNormalizer.Normalize(message));
        Assert.Same(before, message.Contents);
    }

    [Fact]
    public void HistoryLoadNormalizesLegacyMessages()
    {
        // 老会话已经把碎片落了盘,读进来就该是干净的
        string text = HistoryJsonl.SerializeLines([
            new ChatMessage(ChatRole.Assistant,
            [
                new TextContent(""),
                new TextReasoningContent("想"),
                new TextContent(""),
                new TextReasoningContent("完了"),
                new TextContent("答案"),
            ]),
        ]);

        List<ChatMessage> restored = HistoryJsonl.Parse(text.Split('\n'));

        Assert.Collection(Assert.Single(restored).Contents,
            x => Assert.Equal("想完了", Assert.IsType<TextReasoningContent>(x).Text),
            x => Assert.Equal("答案", Assert.IsType<TextContent>(x).Text));
    }
}
