using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.Tests.Agent;
using UiharuMind.Core.Tests.Execution.Extensions;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// 桌面在三档里的审批（ADR 0084）：看不问；动手、打开三档都问，「本会话允许」只对当时那个应用；真实键鼠与接管永远问
/// </summary>
public class DesktopApprovalTests
{
    private const string Root = "/tmp/uiharu-ws";

    private static FunctionCallContent Act(string window, string input = "") =>
        ExtensionCallOf.Make(BuiltinExtensions.Desktop, BuiltinExtensions.DesktopAct,
            new { window, action = "click", target = "e1", input });

    private static Task<bool> ApprovedAsync(EAgentPermissionMode mode, FunctionCallContent call, params string[] sessionApproved)
        => ApprovalRuleProbe.IsApprovedAsync(ApprovalModeMapper.BuildRules(mode, Root,
            sessionExtensionApprovalSource: () => sessionApproved), call);

    [Theory]
    [InlineData(BuiltinExtensions.DesktopWindows)]
    [InlineData(BuiltinExtensions.DesktopSnapshot)]
    public async Task Looking_NeverAsks(string command)
    {
        Assert.True(await ApprovedAsync(EAgentPermissionMode.ReadOnly,
            ExtensionCallOf.Make(BuiltinExtensions.Desktop, command, new { window = "w1" })));
    }

    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task Acting_AsksInEveryMode(EAgentPermissionMode mode)
    {
        Assert.False(await ApprovedAsync(mode, Act("w1")));
        Assert.False(await ApprovedAsync(mode,
            ExtensionCallOf.Make(BuiltinExtensions.Desktop, BuiltinExtensions.DesktopOpen, new { target = "Calculator" })));
    }

    [Fact]
    public async Task SessionApproval_CoversOnlyTheAppItWasGivenFor()
    {
        FunctionCallContent act = Act("w1");
        Assert.True(ExtensionCalls.TryRead(act, out ExtensionInvocation? invocation));
        string key = ExtensionRisk.ApprovalKey(invocation, act);

        Assert.True(await ApprovedAsync(EAgentPermissionMode.ReadOnly, act, key));
        Assert.False(await ApprovedAsync(EAgentPermissionMode.ReadOnly, Act("w2"), key));
        Assert.False(await ApprovedAsync(EAgentPermissionMode.ReadOnly, act, "desktop")); //整个扩展那一格不算
        Assert.True(await ApprovedAsync(EAgentPermissionMode.ReadOnly, act, "*"));
    }

    [Fact]
    public async Task RealInputAndHandoff_AlwaysAsk()
    {
        Assert.False(await ApprovedAsync(EAgentPermissionMode.FullAuto, Act("w1", "real"), "*"));
        Assert.True(ExtensionRisk.AlwaysAsks(Act("w1", "real")));
        Assert.True(ExtensionRisk.AlwaysAsks(ExtensionCallOf.Make(BuiltinExtensions.Desktop, BuiltinExtensions.DesktopHandoff,
            new { window = "w1", task = "type the password" })));
        Assert.False(ExtensionRisk.AlwaysAsks(Act("w1")));
    }
}
