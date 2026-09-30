using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 框架给 shell 工具的默认描述末尾写死了"用户会审批每一次调用"，自动档与预授权下并不成立。
/// 剥这句靠的是字符串替换，框架一改措辞就会静默失效——这条测试就是那道闸。
/// </summary>
public class ShellToolDescriptionTests
{
    [Fact]
    public async Task ShellTool_DoesNotClaimEveryCallIsApproved()
    {
        await using LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);

        AIFunction tool = ShellExecutorFactory.CreateTool(executor);

        Assert.Equal(CharacterRunnerFactory.ShellToolName, tool.Name);
        Assert.DoesNotContain("approves", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shell:", tool.Description); //框架描述的其余部分(系统与 shell 种类)仍保留
        Assert.IsAssignableFrom<ApprovalRequiredAIFunction>(tool);
    }
}
