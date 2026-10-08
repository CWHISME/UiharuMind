using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.Tests.Agent;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 浏览器工具在三档里的审批：看页面不问，导航与动作跟 shell 同一列；
/// 打开本机、内网与非 http(s) 地址是贯穿三档的硬规则，完全自动也要用户点一次
/// </summary>
public class BrowserApprovalTests
{
    private const string Root = "/tmp/uiharu-ws";

    private static FunctionCallContent Navigate(string url) => new("c1", BrowserTools.NavigateToolName,
        new Dictionary<string, object?> { ["url"] = url });

    private static FunctionCallContent Act(string action) => new("c2", BrowserTools.ActToolName,
        new Dictionary<string, object?> { ["action"] = action, ["target"] = "e1" });

    private static Task<bool> ApprovedAsync(EAgentPermissionMode mode, FunctionCallContent call)
        => ApprovalRuleProbe.IsApprovedAsync(ApprovalModeMapper.BuildRules(mode, Root), call);

    [Fact]
    public async Task FullAuto_ApprovesPublicNavigationAndActions()
    {
        Assert.True(await ApprovedAsync(EAgentPermissionMode.FullAuto, Navigate("https://example.com")));
        Assert.True(await ApprovedAsync(EAgentPermissionMode.FullAuto, Act("click")));
    }

    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    public async Task BelowFullAuto_NavigationAndActionsAsk(EAgentPermissionMode mode)
    {
        Assert.False(await ApprovedAsync(mode, Navigate("https://example.com")));
        Assert.False(await ApprovedAsync(mode, Act("fill")));
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://192.168.1.1")]
    [InlineData("file:///etc/passwd")]
    public async Task LocalNavigation_IsNeverAutoApproved(string url)
    {
        Assert.False(await ApprovedAsync(EAgentPermissionMode.FullAuto, Navigate(url)));
    }

    [Theory]
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
