using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;
using Xunit;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// <see cref="ToolJson.CreateFunction"/> 入口的 AOT 守卫（EnsureAotSafe）：
/// 工具参数/返回类型既不在框架 source-gen、又没在 <see cref="ToolJsonContext"/> 声明时直接抛，
/// 漏声明在开发期就炸，不等 AOT 产物才静默挂（7115e9d2 撤手写工具要防的漂移）。
/// </summary>
public class ToolJsonGuardTests
{
    // 不在框架 source-gen、也没进 ToolJsonContext 的类型
    private sealed record UndeclaredPayload(string X);

    [Fact]
    public void CreateFunction_ThrowsWhenATypeIsUndeclared()
    {
        // string 框架 source-gen 认；UndeclaredPayload 两边都缺——该炸，消息指向 ToolJsonContext
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => ToolJson.CreateFunction(
                (string p, UndeclaredPayload payload) => Task.FromResult(p),
                "guard-test",
                "x"));
        Assert.Contains("ToolJsonContext", ex.Message);
        Assert.Contains("UndeclaredPayload", ex.Message);
    }

    [Fact]
    public void CreateFunction_PassesWhenAllTypesAreCovered()
    {
        // string / int / int? 都在框架 source-gen，不声明也过
        AIFunction ok = ToolJson.CreateFunction(
            (string p, int n, int? maybe) => Task.FromResult(p),
            "guard-ok",
            "x");
        Assert.Equal("guard-ok", ok.Name);
    }

    [Fact]
    public void CreateFunction_AcceptsTaskReturnType()
    {
        // Task<string> 返回：内层 string 框架 source-gen 认，过
        AIFunction ok = ToolJson.CreateFunction(
            (string p) => Task.FromResult(p),
            "guard-t",
            "x");
        Assert.Equal("guard-t", ok.Name);
    }
}
