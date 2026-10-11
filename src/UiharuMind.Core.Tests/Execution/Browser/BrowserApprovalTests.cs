using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.Tests.Agent;
using UiharuMind.Core.Tests.Execution.Extensions;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 浏览器在三档里的审批（ADR 0079，经 <see cref="ExtensionCalls.CallName"/>）：看页面不问；导航与动作三档都问，本会话放行过浏览器就不问；
/// 打开本机、内网与非 http(s) 地址、接管永远问，本会话放行也不算
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

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://192.168.1.1")]
    [InlineData("file:///etc/passwd")]
    public async Task LocalNavigation_IsNeverAutoApproved(string url)
    {
        Assert.False(await ApprovedAsync(EAgentPermissionMode.FullAuto, Navigate(url), "*", "browser"));
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
