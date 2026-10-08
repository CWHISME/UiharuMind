using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 发送时投影（ADR 0053）：历史里只有路径，发出去的请求在那一串 tool 消息之后多一条带图的 user 消息。
/// 哪次 Read 算看图只看参数里的路径，结果文本说什么都不算
/// </summary>
public sealed class ImagePreviewProjectionTests : IDisposable
{
    private readonly string _draft = Path.Combine(Path.GetTempPath(), "uiharu-viewproj-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_draft)) Directory.Delete(_draft, recursive: true);
    }

    private AgentPathResolver Paths() => new(null, _draft);

    private string SavePreview(byte[] bytes)
    {
        Directory.CreateDirectory(_draft);
        string path = Path.Combine(_draft, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static FunctionCallContent ReadCall(string callId, string filePath) =>
        new(callId, FileToolNames.Read, new Dictionary<string, object?> { ["filePath"] = filePath });

    private static ChatMessage Call(FunctionCallContent call) => new(ChatRole.Assistant, [call]);

    private static ChatMessage Result(string callId, string result) =>
        new(ChatRole.Tool, [new FunctionResultContent(callId, result)]);

    [Fact]
    public void ImageRead_IsFollowedByAUserMessageCarryingThePreview()
    {
        byte[] png = TestImages.Png(3, 3);
        string preview = SavePreview(png);
        ChatMessage answer = new(ChatRole.Assistant, "a cat");
        List<ChatMessage> history =
        [
            new(ChatRole.User, "look"),
            Call(ReadCall("c1", "cat.png")),
            Result("c1", $"Attached: cat.png\nPreview: {preview}\n"),
            answer,
        ];

        IReadOnlyList<ChatMessage> sent = ImagePreviewProjection.Project(history, Paths());

        Assert.Equal(5, sent.Count);
        Assert.Equal(ChatRole.User, sent[3].Role);
        DataContent image = Assert.Single(sent[3].Contents.OfType<DataContent>());
        Assert.Equal(png, image.Data.ToArray());
        Assert.Same(answer, sent[4]);
        Assert.Equal(4, history.Count); //历史本身不动
    }

    /// <summary>并行调用的多个结果必须连在一起：图插在整串之后，不插进中间；多张图合成一条消息</summary>
    [Fact]
    public void ParallelReads_KeepTheToolRunContiguous_AndShareOneImageMessage()
    {
        string first = SavePreview(TestImages.Png(2, 2));
        string second = SavePreview(TestImages.Png(3, 3));
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, [ReadCall("c1", "a.png"), ReadCall("c2", "notes.md"), ReadCall("c3", "b.JPG")]),
            Result("c1", $"Preview: {first}"),
            Result("c2", "file text"),
            Result("c3", $"Preview: {second}"),
        ];

        IReadOnlyList<ChatMessage> sent = ImagePreviewProjection.Project(history, Paths());

        Assert.Equal([ChatRole.Assistant, ChatRole.Tool, ChatRole.Tool, ChatRole.Tool, ChatRole.User], sent.Select(m => m.Role));
        Assert.Equal(2, sent[4].Contents.OfType<DataContent>().Count());
    }

    /// <summary>
    /// 文本文件里恰好有一行 <c>Preview: 某张图</c>：参数不是图片路径，投影不理它。
    /// 否则读一份文档就会把本地任意文件当图发出去
    /// </summary>
    [Fact]
    public void TextReadWhoseContentLooksLikeAPreview_IsLeftAlone()
    {
        string preview = SavePreview(TestImages.Png(2, 2));
        List<ChatMessage> history = [Call(ReadCall("c1", "notes.md")), Result("c1", $"Preview: {preview}")];

        Assert.Same(history, ImagePreviewProjection.Project(history, Paths()));
    }

    /// <summary>从历史文件读回来的参数与结果都是 JsonElement；预览被删时发一行说明而不是报错</summary>
    [Fact]
    public void ReloadedCall_WithADeletedPreview_BecomesANote()
    {
        string gone = Path.Combine(_draft, "gone.png");
        FunctionCallContent reloadedCall = new("c1", FileToolNames.Read,
            new Dictionary<string, object?> { ["filePath"] = JsonSerializer.SerializeToElement("cat.png") });
        List<ChatMessage> history =
        [
            Call(reloadedCall),
            new(ChatRole.Tool, [new FunctionResultContent("c1", JsonSerializer.SerializeToElement($"Preview: {gone}"))]),
        ];

        ChatMessage attached = ImagePreviewProjection.Project(history, Paths())[2];

        Assert.Empty(attached.Contents.OfType<DataContent>());
        Assert.Contains("gone.png", attached.Text);
    }
}
