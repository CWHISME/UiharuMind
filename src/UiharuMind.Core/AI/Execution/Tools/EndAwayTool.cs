using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>化身为什么结束离席（ADR 0055）</summary>
public enum EAwayEndRequest
{
    /// <summary>离席目标达成</summary>
    Done,

    /// <summary>碰到必须用户本人定的事</summary>
    NeedsUser,
}

/// <summary>化身调 <see cref="EndAwayTool"/> 留下的结束请求</summary>
/// <param name="Reason">原因</param>
/// <param name="Summary">给用户的交代</param>
public sealed record AwayEndRequest(EAwayEndRequest Reason, string Summary);

/// <summary>
/// 化身结束离席（ADR 0055）。只挂给化身。
///
/// 工具本身什么也不改：离席在化身这一轮结束后，从它这一轮的历史里认出这次调用再收场——
/// 结局判定因此是一份纯函数，不靠工具执行时往哪儿登记
/// </summary>
public static class EndAwayTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "EndAway";

    private const string DoneValue = "done";
    private const string NeedsUserValue = "needs_user";

    /// <summary>
    /// 创建结束离席工具
    /// </summary>
    /// <param name="isInfinite">此刻是不是无限模式；null 为永远不是（测试与非离席装配用）。
    /// 无限模式只有用户手动能结束，调这把只会拿到错误</param>
    /// <returns>工具实例</returns>
    public static AITool Create(Func<bool>? isInfinite = null)
    {
        return AIFunctionFactory.Create(
            ([Description("\"done\" when the away goal is reached; \"needs_user\" when something must be decided by the user in person.")]
                string reason,
                [Description("What the user should know when they come back: what was done, or exactly what is waiting for them.")]
                string summary) => Acknowledge(reason, summary, isInfinite?.Invoke() == true),
            ToolName,
            "End the user's away session. The group stops after this turn and the user is notified. " +
            "Only for a reached goal or a decision the user must make in person. " +
            "In infinite away mode this tool is disabled and returns an error.");
    }

    /// <summary>
    /// 从一次工具调用里认出结束请求
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>结束请求；不是本工具或参数认不出为 null</returns>
    public static AwayEndRequest? Read(FunctionCallContent call)
    {
        if (call.Name != ToolName || call.Arguments == null) return null;

        string? reason = Text(call.Arguments.TryGetValue("reason", out object? rawReason) ? rawReason : null);
        string summary = Text(call.Arguments.TryGetValue("summary", out object? rawSummary) ? rawSummary : null)?.Trim()
                         ?? string.Empty;
        return reason?.Trim() switch
        {
            DoneValue => new AwayEndRequest(EAwayEndRequest.Done, summary),
            NeedsUserValue => new AwayEndRequest(EAwayEndRequest.NeedsUser, summary),
            _ => null,
        };
    }

    private static string Acknowledge(string reason, string summary, bool infinite)
    {
        if (infinite)
            return "Error: infinite away mode is on and only the user can end this session. " +
                   "Do not call this tool again; re-analyze the current situation and continue with concrete next steps.";
        return reason.Trim() is DoneValue or NeedsUserValue
            ? "Away session will end after this turn. Do not say anything more to the group."
            : $"Error: reason must be \"{DoneValue}\" or \"{NeedsUserValue}\".";
    }

    // 落盘往返后参数值是 JsonElement
    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => value.ToString(),
    };
}
