using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.Tests.Agent;
using UiharuMind.Core.Tests.Execution.Extensions;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 浏览器在三档里的审批（ADR 0079，经 <see cref="ExtensionCalls.CallName"/>）：看页面不问；导航与动作三档都问，本会话放行过浏览器就不问；
/// 打开本机、内网与非 http(s) 地址、接管永远问，本会话放行也不算——
/// <b>例外</b>是落在本会话界内的 <c>file://</c> 页面（工作区/草稿房间里我们自己生成的拾取器、报告），它按一般级走
/// </summary>
public class BrowserApprovalTests
{
    private const string Root = "/tmp/uiharu-ws";

    private static FunctionCallContent Navigate(string url) =>
        ExtensionCallOf.Make(BuiltinExtensions.Browser, BuiltinExtensions.BrowserNavigate, new { url });

    private static FunctionCallContent Act(string action) =>
        ExtensionCallOf.Make(BuiltinExtensions.Browser, BuiltinExtensions.BrowserAct, new { action, target = "e1" });

    private static Task<bool> ApprovedAsync(EAgentPermissionMode mode, FunctionCallContent call,
        params string[] sessionApproved)
        => ApprovalRuleProbe.IsApprovedAsync(ApprovalModeMapper.BuildRules(mode, Root,
            sessionExtensionApprovalSource: () => sessionApproved), call);

    /// <summary>界内判定要真的绝对路径（file:// 的 LocalPath 带着平台口径），所以单独给一份口径</summary>
    private static Task<bool> ApprovedForPageAsync(EAgentPermissionMode mode, string workspace, string room,
        string url, params string[] sessionApproved)
        => ApprovalRuleProbe.IsApprovedAsync(ApprovalModeMapper.BuildRules(mode, workspace,
            approvedWriteRoot: room, sessionExtensionApprovalSource: () => sessionApproved), Navigate(url));

    private static string UrlOf(string path) => new Uri(path).AbsoluteUri;

    /// <summary>无人在场的规则集（定时运行、离席）：与界面上的放行名单一样，但「只认扩展不再询问」那一档直接拒</summary>
    private static Task<bool> UnattendedAsync(FunctionCallContent call, params string[] sessionApproved)
        => ApprovalRuleProbe.IsApprovedAsync(ApprovalModeMapper.BuildRules(EAgentPermissionMode.FullAuto, Root,
            sessionExtensionApprovalSource: () => sessionApproved,
            unattendedSource: static () => true), call);

    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task Snapshot_NeverAsks(EAgentPermissionMode mode)
    {
        Assert.True(await ApprovedAsync(mode,
            ExtensionCallOf.Make(BuiltinExtensions.Browser, BuiltinExtensions.BrowserSnapshot)));
    }

    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task NavigationAndActions_AskInEveryMode(EAgentPermissionMode mode)
    {
        Assert.False(await ApprovedAsync(mode, Navigate("https://example.com")));
        Assert.False(await ApprovedAsync(mode, Act("fill")));
    }

    [Fact]
    public async Task SessionApproval_LetsNavigationAndActionsThrough()
    {
        Assert.True(await ApprovedAsync(EAgentPermissionMode.ReadOnly, Navigate("https://example.com"), "browser"));
        Assert.True(await ApprovedAsync(EAgentPermissionMode.ReadOnly, Act("click"), "*"));
        Assert.False(await ApprovedAsync(EAgentPermissionMode.ReadOnly, Act("click"), "image")); //放行的是别的扩展
    }

    /// <summary>
    /// 本机服务（localhost、回环、*.local）与工作区之外的本地文件：只认「本会话扩展不再询问」那一格，
    /// 点单个扩展的「本会话允许」不够（读遍本机该是用户亲手拍板的事）
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly, "http://localhost:3000")]
    [InlineData(EAgentPermissionMode.FullAuto, "http://localhost:3000")]
    [InlineData(EAgentPermissionMode.FullAuto, "file:///etc/passwd")]
    public async Task LocalServiceAndOutsideFile_NeedTheSessionWideApproval(EAgentPermissionMode mode, string url)
    {
        Assert.False(await ApprovedAsync(mode, Navigate(url)));
        Assert.False(await ApprovedAsync(mode, Navigate(url), BuiltinExtensions.Browser));
        Assert.True(await ApprovedAsync(mode, Navigate(url), ExtensionCalls.AllExtensions));
    }

    /// <summary>内网设备与非 http(s) 地址照旧恒问：勾了「不再询问」也不放行</summary>
    [Theory]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://10.0.0.5")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    public async Task PrivateNetworkAndNonHttp_StillAskEveryTime(string url)
    {
        Assert.False(await ApprovedAsync(EAgentPermissionMode.FullAuto, Navigate(url), ExtensionCalls.AllExtensions,
            BuiltinExtensions.Browser));
    }

    /// <summary>无人在场（定时运行）：「扩展不再询问」那一档也不能代替人亲手勾——本机服务与界外文件一律拒，公网页面照常放行</summary>
    [Fact]
    public async Task Unattended_RefusesLocalServiceButKeepsTheOrdinaryApproval()
    {
        Assert.False(await UnattendedAsync(Navigate("http://localhost:3000"), ExtensionCalls.AllExtensions));
        Assert.False(await UnattendedAsync(Navigate("file:///etc/passwd"), ExtensionCalls.AllExtensions));
        Assert.True(await UnattendedAsync(Navigate("https://example.com"), ExtensionCalls.AllExtensions));
    }

    /// <summary>接管等的是用户的手：完全自动、放行过浏览器也得等他点「完成了」</summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task Handoff_AlwaysWaitsForTheUser(EAgentPermissionMode mode)
    {
        FunctionCallContent handoff = ExtensionCallOf.Make(BuiltinExtensions.Browser, BuiltinExtensions.BrowserHandoff,
            new { task = "登录 GitHub" });

        Assert.False(await ApprovedAsync(mode, handoff, "*", "browser"));
    }

    /// <summary>
    /// 工作区里的本地页面（拾取器、报告）与「界内写入」同一口径：不再算「打开本机地址」——
    /// 没放行时问一次，放行过浏览器或勾了「不再询问」就不问
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task WorkspaceLocalPage_FollowsTheSessionApproval(EAgentPermissionMode mode)
    {
        string workspace = Path.Combine(Path.GetTempPath(), "uiharu-ws-scope");
        string url = UrlOf(Path.Combine(workspace, "browser", "picker.html"));

        Assert.False(await ApprovedForPageAsync(mode, workspace, string.Empty, url));
        Assert.True(await ApprovedForPageAsync(mode, workspace, string.Empty, url, BuiltinExtensions.Browser));
        Assert.True(await ApprovedForPageAsync(mode, workspace, string.Empty, url, ExtensionCalls.AllExtensions));
    }

    /// <summary>
    /// 带 query/fragment 的界内页面照样算界内：`file:///…/x.html?from=1` 的 LocalPath 会把 query 带进来，
    /// 规范化时先把 ?/# 之后的截掉，不能因此把界内页面判成界外
    /// </summary>
    [Fact]
    public async Task WorkspaceLocalPage_WithQuery_IsStillInScope()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "uiharu-ws-scope");
        string url = UrlOf(Path.Combine(workspace, "picker.html")) + "?from=1#top";

        Assert.True(await ApprovedForPageAsync(EAgentPermissionMode.ReadOnly, workspace, string.Empty, url,
            BuiltinExtensions.Browser));
    }

    /// <summary>草稿房间住在工作区之外，落在里面的页面同样算界内</summary>
    [Fact]
    public async Task DraftRoomLocalPage_IsInScopeToo()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "uiharu-ws-scope");
        string room = Path.Combine(Path.GetTempPath(), "uiharu-room-scope");
        string url = UrlOf(Path.Combine(room, "report.html"));

        Assert.True(await ApprovedForPageAsync(EAgentPermissionMode.ReadOnly, workspace, room, url,
            ExtensionCalls.AllExtensions));
    }

    /// <summary>工作区之外的本机页面与界内不是一档：界外只认全局那一格</summary>
    [Fact]
    public async Task LocalPageOutsideWorkspace_NeedsTheSessionWideApproval()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "uiharu-ws-scope");
        string url = UrlOf(Path.Combine(Path.GetTempPath(), "uiharu-elsewhere", "page.html"));

        Assert.False(await ApprovedForPageAsync(EAgentPermissionMode.ReadOnly, workspace, string.Empty, url));
        Assert.False(await ApprovedForPageAsync(EAgentPermissionMode.ReadOnly, workspace, string.Empty, url,
            BuiltinExtensions.Browser));
        Assert.True(await ApprovedForPageAsync(EAgentPermissionMode.ReadOnly, workspace, string.Empty, url,
            ExtensionCalls.AllExtensions));
    }

    /// <summary>
    /// 浏览器会自己抹平、自己认的写法都要判对：控制字符、反斜杠、<c>file:</c> 的两种斜杠写法、盘符路径、UNC。
    /// 归一化与补协议与执行层共用一份（见 BrowserRisk.ToAbsoluteUrl），建不出来的地址一律恒问
    /// </summary>
    [Theory]
    [InlineData("file:/C:/Windows/win.ini", EExtensionRisk.SessionWideOnly)]
    [InlineData("file:C:/Windows/win.ini", EExtensionRisk.SessionWideOnly)]
    [InlineData("C:/Windows/win.ini", EExtensionRisk.SessionWideOnly)]
    [InlineData("file://host/share/x.html", EExtensionRisk.SessionWideOnly)]
    [InlineData("http:\\localhost", EExtensionRisk.SessionWideOnly)]
    [InlineData("http:localhost", EExtensionRisk.SessionWideOnly)]
    [InlineData("http://local\thost", EExtensionRisk.SessionWideOnly)]
    [InlineData("fi\nle:///C:/secret", EExtensionRisk.SessionWideOnly)]
    [InlineData("//localhost:3000/x", EExtensionRisk.SessionWideOnly)]
    [InlineData("https:///localhost", EExtensionRisk.SessionWideOnly)]
    [InlineData("::::", EExtensionRisk.AlwaysAsk)]
    public void LocalTargets_AreNormalizedTheWayTheBrowserDoes(string url, EExtensionRisk expected)
    {
        Assert.Equal(expected, ExtensionRisk.Classify(Navigate(url)));
    }

    /// <summary>分级上看得见：界内页面一般级；本机服务与界外本地文件要主动勾；内网设备与非 http(s) 恒问</summary>
    [Fact]
    public void LocalTargets_LadderForApproval()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "uiharu-ws-scope");
        string room = Path.Combine(Path.GetTempPath(), "uiharu-room-scope");
        ExtensionRiskContext context = new()
        {
            IsPathInScope = ApprovalModeMapper.InScopePredicate(new AgentPathResolver(workspace, room)),
        };

        Assert.Equal(EExtensionRisk.Standard,
            ExtensionRisk.Classify(Navigate(UrlOf(Path.Combine(workspace, "picker.html"))), context));
        Assert.Equal(EExtensionRisk.Standard,
            ExtensionRisk.Classify(Navigate(UrlOf(Path.Combine(room, "report.html"))), context));
        Assert.Equal(EExtensionRisk.SessionWideOnly,
            ExtensionRisk.Classify(Navigate("http://localhost:3000"), context));
        Assert.Equal(EExtensionRisk.SessionWideOnly,
            ExtensionRisk.Classify(Navigate("file:///etc/passwd"), context));
        Assert.Equal(EExtensionRisk.AlwaysAsk, ExtensionRisk.Classify(Navigate("http://192.168.1.1"), context));
        Assert.Equal(EExtensionRisk.AlwaysAsk,
            ExtensionRisk.Classify(Navigate("data:text/html,<h1>hi</h1>"), context));
    }

    /// <summary>上传把本机文件交给第三方网站，送出去收不回：放行过浏览器也每次问</summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task Upload_AlwaysAsks(EAgentPermissionMode mode)
    {
        FunctionCallContent upload = ExtensionCallOf.Make(BuiltinExtensions.Browser, BuiltinExtensions.BrowserAct,
            new { action = "Upload", target = "e3", value = "report.pdf" });

        Assert.False(await ApprovedAsync(mode, upload, "*", "browser"));
    }

    [Theory]
    [InlineData("data:text/html,<h1>hi</h1>")] //页面内容由调用方写定，与本机地址同样每次都问
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("http://10.0.0.5")]
    [InlineData("http://172.20.1.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("printer.local")]
    [InlineData("file:///etc/passwd")]
    [InlineData("chrome://settings")]
    public void LocalTargets_AreRecognized(string url)
    {
        Assert.True(BrowserRisk.IsLocalTarget(url));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("http://172.32.0.1")]
    [InlineData("https://8.8.8.8")]
    [InlineData("about:blank")]
    public void PublicTargets_AreNot(string url)
    {
        Assert.False(BrowserRisk.IsLocalTarget(url));
    }
}
