using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 从单聊开群时带进群的背景（方案 v6 §6.6b 规则 5 的轻量落地：另开一个群，原单聊不动）。
/// 让原单聊的模型把那段对话写成一份给新成员看的摘要，填进新群的输入框，由用户过目、删改后再发——
/// 新成员看到什么由用户定。普通群的成员没有工具读不了文件，所以背景是正文而不是文件路径。
/// 复用交接文档那条线（同一个客户端与装配好的选项，前缀缓存照吃），只换了写给谁看
/// </summary>
public static class GroupBackground
{
    private const int SummaryCharLimit = 1500; //背景要被每位新成员读一遍，也要让用户看得完

    // 附在交接指令之后，优先级高于它：那份是写给压缩后的自己，这份是写给没看过这段对话的新成员
    internal const string Audience = """
        This document does NOT replace the conversation. It will be shown to other characters who are
        joining a group chat that continues from here as new participants; they have seen none of it.
        Write it for them: what the user wants, what has been established so far, and what is still open.
        Leave out personal details that are not needed to follow the topic.
        HARD LIMIT: at most 1500 characters - this overrides the limit above.
        """;

    /// <summary>
    /// 让原单聊的模型写一份背景摘要
    /// </summary>
    /// <param name="source">原单聊</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>摘要正文；没有模型、没有可写的历史或写失败为 null</returns>
    public static async Task<string?> WriteAsync(ChatSession source, CancellationToken cancellationToken = default)
    {
        if (source.ChatModelRunningData is not { ChatClient: { } client } model) return null;

        ChatOptions? options = source.Runner.ChatOptions;
        IReadOnlyList<ChatMessage> supplied = SuppliedHistory(source.History, options);
        if (supplied.Count == 0) return null;

        string? summary = await HistoryHandoff.WriteAsync(client, supplied, options, model.ContextLength, Audience,
            cancellationToken).ConfigureAwait(false);
        return summary == null ? null : HistoryHandoff.Cap(summary, SummaryCharLimit);
    }

    /// <summary>
    /// 新群输入框里的那段背景：说明来由 + 摘要，末尾留空行给用户接着写想问的
    /// </summary>
    /// <param name="characterName">原单聊的角色名</param>
    /// <param name="summary">摘要正文</param>
    /// <returns>草稿正文</returns>
    public static string Compose(string characterName, string summary) =>
        $"（背景：这个群是从我和{characterName}的单聊开出来的，下面是那段对话的摘要。）\n\n{summary.Trim()}\n\n";

    /// <summary>
    /// 写摘要时交给模型的历史：与平时供给同一口径（从最后一份交接文档起、不带知识库片段）；
    /// 选项里没挂工具时去掉工具内容，否则模型会照着历史再编一个调用（见 <see cref="PromptOnlyHistory"/>）
    /// </summary>
    /// <param name="history">原单聊的完整历史</param>
    /// <param name="options">原单聊装配好的选项；未装配为 null</param>
    /// <returns>要交给模型的历史</returns>
    internal static IReadOnlyList<ChatMessage> SuppliedHistory(IReadOnlyList<ChatMessage> history, ChatOptions? options)
    {
        List<ChatMessage> supplied = history
            .Skip(HistoryHandoff.SupplyStartIndex(history))
            .Where(x => !ChatMessageAnnotations.IsKnowledge(x))
            .ToList();
        return options?.Tools is { Count: > 0 } ? supplied : PromptOnlyHistory.StripToolContents(supplied);
    }
}
