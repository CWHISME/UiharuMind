using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 附件 → 用户消息的组装规则（<see cref="AttachmentTrayViewData.BuildUserMessage"/>）。
/// 回归：文件引用曾作为独立 TextContent 追加，同一条 user 消息出现两份 text，部分
/// OpenAI 兼容网关返回 400；现在引用合并进唯一一份文本块，正文与引用区用 *** 隔开。
/// </summary>
public class AttachmentTrayMessageTests
{
    [Theory]
    [InlineData("", "[Attached file: /a]")]
    [InlineData("你好", "你好\n\n***\n[Attached file: /a]")]
    public void JoinUserTextWithFileReferences_Format(string text, string expected)
    {
        Assert.Equal(expected, AttachmentTrayViewData.JoinUserTextWithFileReferences(text, ["/a"]));
    }

    [Fact]
    public void JoinUserTextWithFileReferences_MultipleRefs_NewlineSeparated()
    {
        Assert.Equal(
            "你好\n\n***\n[Attached file: /a]\n[Attached file: /b]",
            AttachmentTrayViewData.JoinUserTextWithFileReferences("你好", ["/a", "/b"]));
    }

    /// <summary>
    /// 视觉模型 + 图片内联 + 文件引用：引用必须合并进第一条 TextContent，
    /// 不允许同一条 user 消息再出现第二块 text。
    /// </summary>
    [Fact]
    public void ComposeVisionMessage_KeepsSingleTextBlock_WhenFileReferencesExist()
    {
        List<AIContent> contents =
        [
            new TextContent("你好"),
            new DataContent(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),
        ];

        ChatMessage message = AttachmentTrayViewData.ComposeVisionMessage(contents, "你好", ["/tmp/表情/"]);

        TextContent only = Assert.Single(message.Contents.OfType<TextContent>());
        Assert.Equal("你好\n\n***\n[Attached file: /tmp/表情/]", only.Text);
        Assert.Single(message.Contents.OfType<DataContent>()); //图片内联保持
    }

    /// <summary>非视觉发送：附件引用与正文合成同一段文本，与视觉路径同一种拼法</summary>
    [Fact]
    public void BuildUserMessage_NonVision_JoinsReferencesIntoOneText()
    {
        AttachmentTrayViewData tray = new(() => null, () => null);
        tray.AddAttachmentPath("/tmp/a.txt"); //非图片 → 走路径引用
        List<ConversationAttachment>? attachments = tray.TakePending();

        ChatMessage message = tray.BuildUserMessage("你好", attachments);

        Assert.NotNull(message.Text);
        Assert.Contains("你好", message.Text);
        Assert.Contains("[Attached file: /tmp/a.txt]", message.Text);
        Assert.Contains("***", message.Text);
    }

    /// <summary>目录不是文件附件：不能进附件盘（否则没有文件名、也不能预览/读取）</summary>
    [Fact]
    public void AddAttachmentPath_Directory_IsIgnored()
    {
        string dir = Path.Combine(Path.GetTempPath(), "uiharu_tray_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AttachmentTrayViewData tray = new(() => null, () => null);
            tray.AddAttachmentPath(dir);

            Assert.Empty(tray.Attachments);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}