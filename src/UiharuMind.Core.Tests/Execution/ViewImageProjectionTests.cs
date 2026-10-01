using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 发送时投影（ADR 0053）：历史里只有路径，发出去的请求在那一串 tool 消息之后多一条带图的 user 消息
/// </summary>
public sealed class ViewImageProjectionTests : IDisposable
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

    private static ChatMessage Call(string callId, string name) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, name)]);

    private static ChatMessage Result(string callId, string result) =>
        new(ChatRole.Tool, [new FunctionResultContent(callId, result)]);

    [Fact]
    public void ViewImageResult_IsFollowedByAUserMessageCarryingThePreview()
    {
        byte[] png = TestImages.Png(3, 3);
        string preview = SavePreview(png);
        ChatMessage answer = new(ChatRole.Assistant, "a cat");
        List<ChatMessage> history =
        [
            new(ChatRole.User, "look"),
            Call("c1", ViewImageTool.ToolName),
            Result("c1", $"Attached: cat.png\nPreview: {preview}\n"),
            answer,
        ];

        IReadOnlyList<ChatMessage> sent = ViewImageProjection.Project(history, Paths());

        Assert.Equal(5, sent.Count);
        Assert.Equal(ChatRole.User, sent[3].Role);
        DataContent image = Assert.Single(sent[3].Contents.OfType<DataContent>());
        Assert.Equal(png, image.Data.ToArray());
        Assert.Same(answer, sent[4]);
        Assert.Equal(4, history.Count); //历史本身不动
    }

    /// <summary>并行调用的多个结果必须连在一起：图插在整串之后，不插进中间</summary>
    [Fact]
    public void ParallelResults_KeepTheToolRunContiguous()
    {
        string preview = SavePreview(TestImages.Png(2, 2));
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("c1", ViewImageTool.ToolName), new FunctionCallContent("c2", "Read")]),
            Result("c1", $"Preview: {preview}"),
            Result("c2", "file text"),
        ];

        IReadOnlyList<ChatMessage> sent = ViewImageProjection.Project(history, Paths());

        Assert.Equal([ChatRole.Assistant, ChatRole.Tool, ChatRole.Tool, ChatRole.User], sent.Select(m => m.Role));
    }

    [Fact]
    public void OtherToolsAndHistoriesWithoutViewImage_AreLeftAlone()
    {
        string preview = SavePreview(TestImages.Png(2, 2));
        List<ChatMessage> history = [Call("c1", "Read"), Result("c1", $"Preview: {preview}")];

        Assert.Same(history, ViewImageProjection.Project(history, Paths()));
    }

    /// <summary>从历史文件读回来的结果是 JsonElement；预览被删时发一行说明而不是报错</summary>
    [Fact]
    public void ReloadedResult_WithADeletedPreview_BecomesANote()
    {
        string gone = Path.Combine(_draft, "gone.png");
        JsonElement reloaded = JsonSerializer.SerializeToElement($"Preview: {gone}");
        List<ChatMessage> history =
        [
            Call("c1", ViewImageTool.ToolName),
            new(ChatRole.Tool, [new FunctionResultContent("c1", reloaded)]),
        ];

        ChatMessage attached = ViewImageProjection.Project(history, Paths())[2];

        Assert.Empty(attached.Contents.OfType<DataContent>());
        Assert.Contains("gone.png", attached.Text);
    }
}
