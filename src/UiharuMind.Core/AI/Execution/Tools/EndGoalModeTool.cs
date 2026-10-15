using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Single.GoalMode;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>模型调 <see cref="EndGoalModeTool"/> 留下的结束请求</summary>
/// <param name="Reason">原因：只会是 <see cref="EGoalModeEndReason.Done"/> 或 <see cref="EGoalModeEndReason.NeedsUser"/></param>
/// <param name="Summary">给用户的交代</param>
public sealed record GoalModeEndCall(EGoalModeEndReason Reason, string Summary);

/// <summary>
/// 模型结束目标模式（隐藏扩展 goal.end 背后的工具，ADR 0079 扩展机制）。
/// 工具本身什么也不改：目标模式在这一轮结束后，控制器从这一轮的历史里认出这次调用再收场——
/// 结局判定因此是一份纯函数，不靠工具执行时往哪儿登记（与 <see cref="EndAwayTool"/> 同一路）。
/// 只挂在隐藏扩展里：广告与帮助不列，模型只在目标模式开启时经注入消息得知
/// </summary>
public static class EndGoalModeTool
{
    /// <summary>内层工具名。提示词与控制器认它</summary>
    public const string ToolName = "EndGoalMode";

    private const string DoneValue = "done";
    private const string NeedsUserValue = "needs_user";

    /// <summary>创建工具（隐藏扩展装配时用）</summary>
    /// <returns>工具实例</returns>
    public static AITool Create()
    {
        return ToolJson.CreateFunction(
            ([Description("目标达成时填 \"done\"；有必须由用户本人决定的事时填 \"needs_user\"。")]
                string reason,
                [Description("用户回来时应当知道的事：完成了什么，或具体有什么在等他们。")]
                string summary) => Acknowledge(reason, summary),
            ToolName,
            "结束目标模式。本轮结束后会话停止自动唤醒，并通知用户。只用于目标达成或必须由用户本人决定的事。");
    }

    /// <summary>
    /// 从一次内层工具调用里认出结束请求（调用方先用 <see cref="Extensions.ExtensionCalls.Unwrap"/> 拆扩展）
    /// </summary>
    /// <param name="call">内层工具调用</param>
    /// <returns>结束请求；不是本工具或参数认不出为 null</returns>
    public static GoalModeEndCall? Read(FunctionCallContent call)
    {
        if (call.Name != ToolName || call.Arguments == null) return null;
        string? reason = Text(call.Arguments.TryGetValue("reason", out object? rawReason) ? rawReason : null);
        string summary = Text(call.Arguments.TryGetValue("summary", out object? rawSummary) ? rawSummary : null)?.Trim()
                         ?? string.Empty;
        return reason?.Trim() switch
        {
            DoneValue => new GoalModeEndCall(EGoalModeEndReason.Done, summary),
            NeedsUserValue => new GoalModeEndCall(EGoalModeEndReason.NeedsUser, summary),
            _ => null,
        };
    }

    private static string Acknowledge(string reason, string summary)
    {
        return reason.Trim() is DoneValue or NeedsUserValue
            ? "目标模式将在本轮结束后结束。之后不要再调用任何工具，简单收尾即可。"
            : $"Error: reason 必须是 \"{DoneValue}\" 或 \"{NeedsUserValue}\"。";
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
