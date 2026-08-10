using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死图片开窗：只有最近几条带图消息真的带图，更早的降级成占位文本。
/// 承重之处在于「不得原地修改历史消息」——会话文件是权威来源，
/// 这里一旦改到原对象，磁盘上的图片就没了。
/// </summary>
public class HistoryImageWindowTests
{
    private static ChatMessage ImageMessage(string text) =>
        new(ChatRole.User, [new DataContent(new byte[] { 1, 2, 3 }, "image/png"), new TextContent(text)]);

    private static ChatMessage TextMessage(string text) => new(ChatRole.Assistant, text);

    [Fact]
    public void WithinWindow_NothingChanges()
    {
        List<ChatMessage> history = [ImageMessage("a"), TextMessage("reply"), ImageMessage("b")];

        List<ChatMessage> result = HistoryImageWindow.DemoteOldImages(history, 2).ToList();

        Assert.Equal(history, result); //原样返回同一批对象
    }

    [Fact]
    public void OlderImages_AreReplacedByPlaceholder()
    {
        List<ChatMessage> history =
            [ImageMessage("oldest"), ImageMessage("middle"), TextMessage("reply"), ImageMessage("newest")];

        List<ChatMessage> result = HistoryImageWindow.DemoteOldImages(history, 2).ToList();

        Assert.DoesNotContain(result[0].Contents, x => x is DataContent); //最老的那条被降级
        Assert.Contains(result[1].Contents, x => x is DataContent); //窗口内的两条保留原图
        Assert.Contains(result[3].Contents, x => x is DataContent);
    }

    [Fact]
    public void DemotedMessage_KeepsItsTextAndRole()
    {
        List<ChatMessage> history = [ImageMessage("说明文字"), ImageMessage("b"), ImageMessage("c")];

        List<ChatMessage> result = HistoryImageWindow.DemoteOldImages(history, 2).ToList();

        Assert.Equal(ChatRole.User, result[0].Role);
        Assert.Contains(result[0].Contents.OfType<TextContent>(), x => x.Text == "说明文字");
        Assert.Contains(result[0].Contents.OfType<TextContent>(), x => x.Text.Contains("omitted"));
    }

    [Fact]
    public void OriginalHistory_IsNeverMutated()
    {
        ChatMessage oldest = ImageMessage("oldest");
        List<ChatMessage> history = [oldest, ImageMessage("b"), ImageMessage("c")];

        HistoryImageWindow.DemoteOldImages(history, 2).ToList();

        Assert.Contains(oldest.Contents, x => x is DataContent); //会话文件里的图片必须还在
        Assert.Equal(2, oldest.Contents.Count);
    }

    [Fact]
    public void MultipleImagesInOneMessage_CollapseToASinglePlaceholder()
    {
        ChatMessage many = new(ChatRole.User, [
            new DataContent(new byte[] { 1 }, "image/png"),
            new DataContent(new byte[] { 2 }, "image/png"),
            new TextContent("两张图"),
        ]);
        List<ChatMessage> history = [many, ImageMessage("b"), ImageMessage("c")];

        List<ChatMessage> result = HistoryImageWindow.DemoteOldImages(history, 2).ToList();

        Assert.Equal(2, result[0].Contents.Count); //一句占位 + 原文
        Assert.Single(result[0].Contents.OfType<TextContent>(), x => x.Text.Contains("omitted"));
    }

    [Fact]
    public void NonImageData_IsLeftAlone()
    {
        ChatMessage withPdf = new(ChatRole.User,
            [new DataContent(new byte[] { 1 }, "application/pdf"), new TextContent("文件")]);
        List<ChatMessage> history = [withPdf, ImageMessage("b"), ImageMessage("c"), ImageMessage("d")];

        List<ChatMessage> result = HistoryImageWindow.DemoteOldImages(history, 2).ToList();

        Assert.Contains(result[0].Contents, x => x is DataContent); //只清图片,别的二进制内容不动
    }

    [Fact]
    public void EmptyHistory_IsHandled()
    {
        Assert.Empty(HistoryImageWindow.DemoteOldImages([], 2));
    }
}
