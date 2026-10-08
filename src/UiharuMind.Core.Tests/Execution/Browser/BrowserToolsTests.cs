using Microsoft.Extensions.AI;
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

    private static IReadOnlyList<AITool> Create(FakeHost host) =>
        BrowserTools.Create(host, "session", Path.Combine(Path.GetTempPath(), "uiharu-browser-tests"));

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

    private sealed class FakeHost : IBrowserHost
    {
        private string _snapshot = "- textbox \"Name\" [ref=e1]\n- button \"Go\" [ref=e2]";

        public List<BrowserActionRequest> Actions { get; } = new();

        public string LastUrl { get; private set; } = string.Empty;

        public Task<BrowserPageView> NavigateAsync(string sessionId, string url, CancellationToken cancellationToken)
        {
            LastUrl = url;
            return Task.FromResult(View());
        }

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
