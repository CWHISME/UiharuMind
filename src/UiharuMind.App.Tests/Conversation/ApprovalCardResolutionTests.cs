using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 审批卡收起后那一行显示的是文案，不是决定代号（从前直接露出 once / deny）
/// </summary>
public class ApprovalCardResolutionTests
{
    private static ToolApprovalRequestContent Request() => new("c1", new FunctionCallContent("c1", "Shell", null));

    [Fact]
    public void ClickedDecision_ShowsItsLabel()
    {
        ApprovalRequestItem card = new(Request());

        card.ResolveCommand.Execute("deny");

        Assert.True(card.IsResolved);
        Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedDenied), card.ResolvedText);
    }

    /// <summary>别处（群的待审批条）先批了：这张收起，显示那个决定</summary>
    [Fact]
    public void DecidedElsewhere_FoldsWithThatDecision()
    {
        ToolApprovalRequestContent request = Request();
        ApprovalRequestItem card = new(request);

        card.MarkDecidedElsewhere(new ChatMessage(ChatRole.User,
            [ToolApprovalResponseFactory.Create(request, EApprovalDecision.Once, "ok")]));

        Assert.True(card.IsResolved);
        Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedOnce), card.ResolvedText);
        Assert.True(card.Response.IsCompleted);
    }

    // 浏览器经扩展调用（ADR 0079）：卡片拆开画，认的还是原工具名
    private static ToolApprovalRequestContent Browser(string tool, params (string Key, object? Value)[] args) =>
        Extension(BuiltinExtensions.Browser, BuiltinExtensions.CanonicalCommand(BuiltinExtensions.Browser, tool)!, args);

    private static ToolApprovalRequestContent Extension(string extension, string command,
        params (string Key, object? Value)[] args) =>
        new("r1", new FunctionCallContent("c1", ExtensionCalls.CallName, new Dictionary<string, object?>
        {
            ["extension"] = extension,
            ["command"] = command,
            ["arguments"] = JsonSerializer.SerializeToElement(args.ToDictionary(x => x.Key, x => x.Value)),
        }));

    /// <summary>扩展卡的「本会话允许」按扩展记，本次按普通允许回应——交给框架按工具名记就把整个 <see cref="ExtensionCalls.CallName"/> 放行了</summary>
    [Fact]
    public async Task ExtensionCard_SessionApproval_IsRememberedPerExtension()
    {
        List<string> approved = new();
        ApprovalRequestItem card = new(Extension(BuiltinExtensions.Image, BuiltinExtensions.ImageGenerate, ("prompt", "a cat")))
        {
            ApproveExtensionCallback = approved.Add,
        };

        card.ResolveCommand.Execute("session");

        Assert.Equal(ImageGenerationTool.ToolName, card.ToolName);
        Assert.Equal(["image"], approved);
        Assert.Contains(Loc.Text(LangKey.ExtensionNameImage), card.SessionButtonText);
        ToolApprovalResponseContent response = Assert.Single((await card.Response).Contents.OfType<ToolApprovalResponseContent>());
        Assert.True(response.Approved);
        Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedSessionExtension, Loc.Text(LangKey.ExtensionNameImage)),
            card.ResolvedText);
    }

    /// <summary>同一次扩展放行在另一处（群待审批条、子会话窗口）先点了：这张也说「本会话允许」，不说「本次」</summary>
    [Fact]
    public async Task ExtensionSessionApproval_DecidedElsewhere_KeepsItsLabel()
    {
        ToolApprovalRequestContent request = Extension(BuiltinExtensions.Image, BuiltinExtensions.ImageGenerate, ("prompt", "a"));
        ApprovalRequestItem clicked = new(request);
        ApprovalRequestItem elsewhere = new(request);

        clicked.ResolveCommand.Execute("session");
        elsewhere.MarkDecidedElsewhere(await clicked.Response);

        Assert.Equal(clicked.ResolvedText, elsewhere.ResolvedText);
        Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedSessionExtension, Loc.Text(LangKey.ExtensionNameImage)),
            elsewhere.ResolvedText);
    }

    /// <summary>每次都要问的（打开本机地址）不给「本会话允许」，传来 session 也只按这一次算、不记</summary>
    [Fact]
    public void LocalNavigationCard_CannotBeApprovedForTheSession()
    {
        List<string> approved = new();
        ApprovalRequestItem card = new(Browser(BrowserTools.NavigateToolName, ("url", "http://192.168.1.1")),
            sessionId: "s1", browser: new FakeBrowser())
        {
            ApproveExtensionCallback = approved.Add,
        };

        card.ResolveCommand.Execute("session");

        Assert.True(card.AsksEveryTime);
        Assert.False(card.CanApproveForSession);
        Assert.Empty(approved);
        Assert.Equal(Loc.Text(LangKey.AgentApprovalResolvedOnce), card.ResolvedText);
    }

    /// <summary>接管要的是用户的手：不给「本会话允许」，别的入口传来 session 也只按这一次算</summary>
    [Fact]
    public async Task HandoffCard_NeverGrantsStandingApproval()
    {
        ApprovalRequestItem card = new(Browser(BrowserTools.HandoffToolName, ("task", "登录 GitHub")),
            sessionId: "s1", browser: new FakeBrowser());

        card.ResolveCommand.Execute("session");

        ToolApprovalResponseContent response = Assert.Single((await card.Response).Contents.OfType<ToolApprovalResponseContent>());
        Assert.True(response.Approved);
        Assert.True(card.IsBrowserHandoff);
        Assert.False(card.CanApproveForSession);
        Assert.Equal(Loc.Text(LangKey.BrowserHandoffResolvedDone), card.ResolvedText);
    }

    [Fact]
    public void CancelledHandoff_SaysCancelled()
    {
        ApprovalRequestItem card = new(Browser(BrowserTools.HandoffToolName, ("task", "登录")), sessionId: "s1",
            browser: new FakeBrowser());

        card.ResolveCommand.Execute("deny");

        Assert.Equal(Loc.Text(LangKey.BrowserHandoffResolvedCancelled), card.ResolvedText);
    }

    /// <summary>打不开浏览器（没装 Chrome、档案被占）说在卡上，不冲到界面线程；无窗口运行也如实说</summary>
    [Fact]
    public async Task ShowBrowser_ReportsFailuresOnTheCard()
    {
        ApprovalRequestItem broken = new(Browser(BrowserTools.HandoffToolName, ("task", "登录")), sessionId: "s1",
            browser: new FakeBrowser { ShowFailure = new InvalidOperationException("Chrome not found") });
        ApprovalRequestItem headless = new(Browser(BrowserTools.HandoffToolName, ("task", "登录")), sessionId: "s1",
            browser: new FakeBrowser { CanShow = false });

        await broken.ShowHandoffTargetCommand.ExecuteAsync(null);
        await headless.ShowHandoffTargetCommand.ExecuteAsync(null);

        Assert.Equal(Loc.Text(LangKey.BrowserHandoffShowFailed, "Chrome not found"), broken.HandoffStatus);
        Assert.Equal(Loc.Text(LangKey.BrowserHandoffNoWindow), headless.HandoffStatus);
    }

    /// <summary>浏览器调用按会话的快照说人话；晚开的窗口补画早已决出的卡时退回原始参数（页面早往下走了）</summary>
    [Fact]
    public void BrowserCalls_AreDescribed_UntilRedrawnAfterTheDecision()
    {
        ApprovalRequestItem card = new(Browser(BrowserTools.ActToolName, ("action", "click"), ("target", "e2")),
            sessionId: "s1", browser: new FakeBrowser());

        Assert.True(card.IsDescribed);
        Assert.StartsWith(Loc.Text(LangKey.BrowserCallClick,
            Loc.Text(LangKey.BrowserElementNamed, Loc.Text(LangKey.BrowserRoleButton), "Go")), card.ArgumentSummary);

        card.UseRecordedArguments();

        Assert.False(card.IsDescribed);
        Assert.Contains("e2", card.ArgumentSummary);
    }

    /// <summary>别的卡不碰浏览器宿主：从没用过浏览器的人不该为它起计时器</summary>
    [Fact]
    public void OtherTools_LeaveTheBrowserAlone()
    {
        FakeBrowser browser = new();

        ApprovalRequestItem card = new(Request(), sessionId: "s1", browser: browser);

        Assert.False(card.IsDescribed);
        Assert.Equal(0, browser.Calls);
    }

    private sealed class FakeBrowser : IBrowserHost
    {
        public int Calls { get; private set; }

        public bool CanShow { get; init; } = true;

        public Exception? ShowFailure { get; init; }

        public bool HasWindow => CanShow;

        public Task<BrowserPageView> NavigateAsync(string sessionId, string url, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserPageView> SnapshotAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserPageView> ActAsync(string sessionId, BrowserActionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserTextView> ReadTextAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BrowserScreenshot> ScreenshotAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ShowAsync(string sessionId)
        {
            Calls++;
            return ShowFailure != null ? Task.FromException<bool>(ShowFailure) : Task.FromResult(CanShow);
        }

        public BrowserElement? PeekElement(string sessionId, string target)
        {
            Calls++;
            return target == "e2" ? new BrowserElement("button", "Go", false) : null;
        }

        public void UseDownloadDirectory(string sessionId, string directory)
        {
        }

        public string? PeekUrl(string sessionId)
        {
            Calls++;
            return "https://example.com/";
        }
    }
}
