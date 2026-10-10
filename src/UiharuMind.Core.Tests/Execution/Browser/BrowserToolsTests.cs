using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 工具门面对着假宿主测：弱模型写歪的参数照收、缺按键当场拦下、
/// 原地变化只回变了的行、紧跟着的重拍只说没变——快照每一步都进历史，省下的都要重发很多轮
/// </summary>
public class BrowserToolsTests
{
    private static async Task<string> InvokeAsync(IReadOnlyList<AITool> tools, string name, AIFunctionArguments arguments)
    {
        AIFunction function = tools.OfType<AIFunction>().Single(t => t.Name == name);
        return (await function.InvokeAsync(arguments))?.ToString() ?? string.Empty;
    }

    private static readonly string SpillDirectory = Path.Combine(Path.GetTempPath(), "uiharu-browser-tests");

    private static IReadOnlyList<AITool> Create(FakeHost host, EReadImageMode screenshots = EReadImageMode.None) =>
        BrowserTools.Create(host, "session", SpillDirectory, screenshots);

    [Fact]
    public async Task ValueWrittenUnderTheActionsName_IsAccepted()
    {
        FakeHost host = new();

        await InvokeAsync(Create(host), BrowserTools.ActToolName,
            new AIFunctionArguments { ["action"] = "press", ["target"] = "e2", ["press"] = "Tab" });

        Assert.Equal("Tab", host.Actions.Single().Value);
    }

    [Fact]
    public async Task PressWithoutAKey_IsRejectedBeforeReachingTheBrowser()
    {
        FakeHost host = new();

        string result = await InvokeAsync(Create(host), BrowserTools.ActToolName,
            new AIFunctionArguments { ["action"] = "press", ["target"] = "e2" });

        Assert.StartsWith("[Error] press needs value", result);
        Assert.Empty(host.Actions);
    }

    [Fact]
    public async Task InPlaceChanges_AreSentAsChangedLines_AndAnImmediateResnapshotAsUnchanged()
    {
        FakeHost host = new();
        IReadOnlyList<AITool> tools = Create(host);

        string opened = await InvokeAsync(tools, BrowserTools.NavigateToolName,
            new AIFunctionArguments { ["url"] = "example.com" });
        string filled = await InvokeAsync(tools, BrowserTools.ActToolName,
            new AIFunctionArguments { ["action"] = "fill", ["target"] = "e1", ["value"] = "Kuroko" });
        string again = await InvokeAsync(tools, BrowserTools.SnapshotToolName, new AIFunctionArguments());

        Assert.Equal("https://example.com", host.LastUrl);
        Assert.Contains("- button \"Go\" [ref=e2]", opened);
        Assert.Contains("Changed lines", filled);
        Assert.Contains("[ref=e1]: Kuroko", filled);
        Assert.DoesNotContain("button \"Go\"", filled);
        Assert.Contains("(unchanged since the previous browser result", again);
    }

    [Fact]
    public async Task TextView_KeepsLinkTextOnly_AndFindReturnsWholeParagraphs()
    {
        FakeHost host = new()
        {
            Text = "# 白井黑子\n\n她是[风纪委员](/wiki/%E9%A2%A8%E7%B4%80)。\n\n能力是空间移动。\n\n就读常盘台中学。",
        };
        IReadOnlyList<AITool> tools = Create(host);

        string whole = await InvokeAsync(tools, BrowserTools.SnapshotToolName,
            new AIFunctionArguments { ["view"] = "text" });
        string found = await InvokeAsync(tools, BrowserTools.SnapshotToolName,
            new AIFunctionArguments { ["view"] = "text", ["find"] = "能力" });

        Assert.Contains("她是风纪委员。", whole);
        Assert.DoesNotContain("/wiki/", whole);
        Assert.EndsWith("能力是空间移动。", found.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("风纪委员", found);
    }

    /// <summary>正文超长时先指 find：通用的「用 Read 接着读」会把模型引去整份读进来</summary>
    [Fact]
    public async Task LongText_PointsToFindBeforeReading()
    {
        string paragraph = new('字', 400);
        FakeHost host = new() { Text = string.Join("\n\n", Enumerable.Repeat(paragraph, 60)) };

        string result = await InvokeAsync(Create(host), BrowserTools.SnapshotToolName,
            new AIFunctionArguments { ["view"] = "text" });

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result) < 12 * 1024);
        Assert.Contains("with view \"text\" and find", result);
        Assert.Contains("BrowserText_", result);
    }

    /// <summary>打开时的旁注（加载超时）是在打开那一步攒下的，按正文返回时也得带上</summary>
    [Fact]
    public async Task NavigatingToTheTextView_KeepsTheNotesFromOpening()
    {
        FakeHost host = new() { Text = "Body", OpenNotes = ["The page was still loading after 30 s."] };

        string result = await InvokeAsync(Create(host), BrowserTools.NavigateToolName,
            new AIFunctionArguments { ["url"] = "example.com", ["view"] = "text" });

        Assert.StartsWith("The page was still loading", result);
        Assert.Contains("Body", result);
        Assert.DoesNotContain("[ref=", result);
    }

    [Fact]
    public async Task UnknownView_IsRejected()
    {
        string result = await InvokeAsync(Create(new FakeHost()), BrowserTools.SnapshotToolName,
            new AIFunctionArguments { ["view"] = "html" });

        Assert.StartsWith("[Error] Unknown view", result);
    }

    [Theory]
    [InlineData(EReadImageMode.Attach, "Read it to look at it")]
    [InlineData(EReadImageMode.PointToAnalyzeImage, "Pass it to AnalyzeImage")]
    public async Task Screenshot_IsSaved_WithTheWayToLookAtIt(EReadImageMode mode, string hint)
    {
        string result = await InvokeAsync(Create(new FakeHost(), mode), BrowserTools.SnapshotToolName,
            new AIFunctionArguments { ["view"] = "screenshot" });

        string path = result.Split("saved to ")[1].Split(".jpg")[0] + ".jpg";
        Assert.True(File.Exists(path));
        Assert.Contains(hint, result);
    }

    [Fact]
    public async Task Screenshot_IsNotOffered_WhenTheConversationCannotSeeImages()
    {
        IReadOnlyList<AITool> tools = Create(new FakeHost());

        string result = await InvokeAsync(tools, BrowserTools.SnapshotToolName,
            new AIFunctionArguments { ["view"] = "screenshot" });

        Assert.StartsWith("[Error] Screenshots are off here", result);
        AIFunction snapshot = tools.OfType<AIFunction>().Single(t => t.Name == BrowserTools.SnapshotToolName);
        Assert.DoesNotContain("screenshot", snapshot.Description);
        Assert.DoesNotContain("screenshot", snapshot.JsonSchema.GetRawText());
    }

    /// <summary>接管的执行发生在用户点「完成了」之后：交回整页，说清是用户动过的</summary>
    [Fact]
    public async Task Handoff_ReturnsThePageAsTheUserLeftIt()
    {
        IReadOnlyList<AITool> tools = Create(new FakeHost());

        string result = await InvokeAsync(tools, BrowserTools.HandoffToolName,
            new AIFunctionArguments { ["task"] = "登录 GitHub" });

        // 不在这一层包审批：浏览器只经 ExtensionCall 调，审批在那一层逐条判（ADR 0079）
        Assert.IsNotType<ApprovalRequiredAIFunction>(tools.Single(t => t.Name == BrowserTools.HandoffToolName));
        Assert.StartsWith("The user took over the browser and handed it back.", result);
        Assert.Contains("- button \"Go\" [ref=e2]", result);
    }

    /// <summary>审批卡的预览：编号按最近一份快照翻回元素，值的别名照收，导航补上协议</summary>
    [Fact]
    public void Preview_ResolvesTheTargetFromTheLatestSnapshot()
    {
        FakeHost host = new();
        FunctionCallContent act = new("c1", BrowserTools.ActToolName,
            new Dictionary<string, object?> { ["action"] = "Press", ["target"] = "ref=e2", ["press"] = "Enter" });
        FunctionCallContent navigate = new("c2", BrowserTools.NavigateToolName,
            new Dictionary<string, object?> { ["url"] = "example.com" });

        BrowserCallPreview? acted = BrowserTools.Preview(act, host, "session");
        BrowserCallPreview? opened = BrowserTools.Preview(navigate, host, "session");

        Assert.Equal(new BrowserElement("button", "Go", false), acted?.Element);
        Assert.Equal("press", acted?.Action);
        Assert.Equal("Enter", acted?.Value);
        Assert.Equal("https://example.com/", acted?.PageUrl);
        Assert.Equal("https://example.com", opened?.Value);
        Assert.Null(BrowserTools.Preview(new FunctionCallContent("c3", "Read"), host, "session"));
    }

    private sealed class FakeHost : IBrowserHost
    {
        private string _snapshot = "- textbox \"Name\" [ref=e1]\n- button \"Go\" [ref=e2]";

        public List<BrowserActionRequest> Actions { get; } = new();

        public string LastUrl { get; private set; } = string.Empty;

        public string Text { get; init; } = string.Empty;

        public IReadOnlyList<string> OpenNotes { get; init; } = [];

        public bool HasWindow => true;

        public Task<BrowserPageView> NavigateAsync(string sessionId, string url, CancellationToken cancellationToken)
        {
            LastUrl = url;
            return Task.FromResult(View() with { Notes = OpenNotes });
        }

        public Task<BrowserTextView> ReadTextAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserTextView("https://example.com/", "Example", Text, []));

        public Task<BrowserScreenshot> ScreenshotAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserScreenshot("https://example.com/", "Example", [0xFF, 0xD8, 0xFF, 0xE0], []));

        public Task<bool> ShowAsync(string sessionId) => Task.FromResult(true);

        public BrowserElement? PeekElement(string sessionId, string target) =>
            AxSnapshotSearch.NormalizeTarget(target) == "e2" ? new BrowserElement("button", "Go", false) : null;

        public string? PeekUrl(string sessionId) => "https://example.com/";

        public Task<BrowserPageView> SnapshotAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(View());

        public Task<BrowserPageView> ActAsync(string sessionId, BrowserActionRequest request,
            CancellationToken cancellationToken)
        {
            Actions.Add(request);
            if (request.Action == EBrowserAction.Fill)
            {
                _snapshot = _snapshot.Replace("[ref=e1]", "[ref=e1]: " + request.Value);
            }

            return Task.FromResult(View());
        }

        private BrowserPageView View() => new("https://example.com/", "Example", _snapshot, _snapshot, []);
    }
}
