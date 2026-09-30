using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools.Scheduler;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉住定时任务工具的可选参数。时间参数二选一，从前两者都没有默认值，框架按必填处理：
/// 模型只给 delayMinutes 时拿回的是一句框架异常（缺少必填参数 fireAtIso），而不是任务被登记。
/// </summary>
public class ScheduleTaskToolTests
{
    private readonly AIFunction _tool = (AIFunction)SchedulerTools.CreateScheduledTaskTool(null);

    [Fact]
    public void Schema_RequiresOnlyNameAndPrompt()
    {
        string?[] required = _tool.JsonSchema.GetProperty("required").EnumerateArray()
            .Select(x => x.GetString()).ToArray();

        Assert.Equal(["displayName", "prompt"], required);
    }

    /// <summary>两个时间参数都不给也要走到工具自己的话术，而不是在参数绑定时抛异常（此路径不会真的登记任务）</summary>
    [Fact]
    public async Task OmittedOptionalParameters_ReachTheToolInsteadOfThrowing()
    {
        object? result = await _tool.InvokeAsync(
            new AIFunctionArguments { ["displayName"] = "commit", ["prompt"] = "commit the repo" },
            TestContext.Current.CancellationToken);

        string text = result is JsonElement element ? element.GetString() ?? string.Empty : result?.ToString() ?? string.Empty;
        Assert.Contains("provide either delayMinutes", text);
    }
}
