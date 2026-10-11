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
/// <param name="Say">点名要进群的告别话；没有为 null</param>
public sealed record AwayEndRequest(EAwayEndRequest Reason, string Summary, string? Say = null);

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
            ([Description("离席目标达成时填 \"done\"；有必须由用户本人决定的事时填 \"needs_user\"。")]
                string reason,
                [Description("用户回来时应当知道的事：完成了什么，或具体有什么在等他们。")]
                string summary,
                [Description("结束前可选地对群说几句告别话；没有要告诉群的就留空。" +
                    "本调用旁边的任何文字都只留在本地，只有这个参数会到达群里。")]
                string? say = null) => Acknowledge(reason, summary, isInfinite?.Invoke() == true),
            ToolName,
            "结束用户的离席会话。本轮结束后群停止，并通知用户。" +
            "只用于目标达成或必须由用户本人决定的事。" +
            "无限离席模式下本工具被禁用，会返回错误。");
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
        string? say = Text(call.Arguments.TryGetValue("say", out object? rawSay) ? rawSay : null)?.Trim();
        return reason?.Trim() switch
        {
            DoneValue => new AwayEndRequest(EAwayEndRequest.Done, summary, say),
            NeedsUserValue => new AwayEndRequest(EAwayEndRequest.NeedsUser, summary, say),
            _ => null,
        };
    }

    private static string Acknowledge(string reason, string summary, bool infinite)
    {
        if (infinite)
            return "Error: 无限离席模式已开启，只有用户能结束这个会话。" +
                   "不要再调用本工具；重新分析当前情况并继续推进具体步骤。";
        return reason.Trim() is DoneValue or NeedsUserValue
            ? "离席会话将在本轮结束后结束。之后不要再对群说任何话。"
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
