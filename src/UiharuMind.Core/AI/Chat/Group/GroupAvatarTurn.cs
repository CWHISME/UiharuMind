using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>化身一轮的结局（ADR 0055）</summary>
public enum EGroupAvatarTurnResult
{
    /// <summary>说了话：以用户的名义发进群，开下一波</summary>
    Pushed,

    /// <summary>调了结束离席</summary>
    Ended,

    /// <summary>没给出下文：沉默、空回复、撞轮数上限、或只是嘴上收住</summary>
    Silent,

    /// <summary>出错</summary>
    Failed,

    /// <summary>被用户停下</summary>
    Stopped,

    /// <summary>没跑：用户正在私聊化身（闸被占着）</summary>
    Busy,
}

/// <summary>
/// 化身一轮的结果
/// </summary>
/// <param name="Result">结局</param>
/// <param name="End">调了结束离席时的请求；否则为 null</param>
public sealed record GroupAvatarTurn(EGroupAvatarTurnResult Result, AwayEndRequest? End = null)
{
    /// <summary>没跑（私聊占着闸）</summary>
    public static GroupAvatarTurn Busy { get; } = new(EGroupAvatarTurnResult.Busy);

    /// <summary>
    /// 判定化身这一轮的结局：结束离席优先（调了它之后再说的话不进群），其次是被停（停止不能被它已说出的话绕过），
    /// 再看有没有话进了群
    /// </summary>
    /// <param name="turnMessages">化身这一轮新增的消息</param>
    /// <param name="completed">这一轮是否正常跑完</param>
    /// <param name="stopped">是否被用户停下</param>
    /// <param name="posted">进了群的条数</param>
    /// <returns>结局</returns>
    public static GroupAvatarTurn Classify(IEnumerable<ChatMessage> turnMessages, bool completed, bool stopped,
        int posted)
    {
        if (FindEnd(turnMessages) is { } end) return new GroupAvatarTurn(EGroupAvatarTurnResult.Ended, end);
        if (stopped) return new GroupAvatarTurn(EGroupAvatarTurnResult.Stopped);
        if (posted > 0) return new GroupAvatarTurn(EGroupAvatarTurnResult.Pushed);
        return new GroupAvatarTurn(completed ? EGroupAvatarTurnResult.Silent : EGroupAvatarTurnResult.Failed);
    }

    /// <summary>
    /// 这一段消息里化身有没有调结束离席
    /// </summary>
    /// <param name="messages">消息</param>
    /// <returns>第一次认得出的结束请求；没有为 null</returns>
    public static AwayEndRequest? FindEnd(IEnumerable<ChatMessage> messages) => Calls(messages)
            .Select(EndAwayTool.Read)
            .FirstOrDefault(x => x != null);

    /// <summary>
    /// 化身自己动手做了什么（进离席回执）：除结束离席外的每次工具调用，工具名 + 路径或命令
    /// </summary>
    /// <param name="messages">化身的消息</param>
    /// <returns>一行一件，按先后</returns>
    public static IReadOnlyList<string> ActionsOf(IEnumerable<ChatMessage> messages) => Calls(messages)
        .Where(x => x.Name != EndAwayTool.ToolName)
        .Select(Describe)
        .ToList();

    private static string Describe(FunctionCallContent call)
    {
        const int maxCommand = 80;
        string? target = ApprovalModeMapper.ExtractFilePath(call.Arguments);
        if (target == null && call.Arguments?.TryGetValue("command", out object? command) == true)
        {
            target = command?.ToString()?.Trim();
            if (target is { Length: > maxCommand }) target = target[..maxCommand] + "…";
        }

        return string.IsNullOrEmpty(target) ? call.Name : $"{call.Name} {target}";
    }

    private static IEnumerable<FunctionCallContent> Calls(IEnumerable<ChatMessage> messages) =>
        messages
            .Where(x => x.Role == ChatRole.Assistant)
            .SelectMany(x => x.Contents.OfType<FunctionCallContent>());
}
