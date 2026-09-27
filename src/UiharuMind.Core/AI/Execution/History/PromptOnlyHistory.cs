using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 普通对话形态（prompt-only，不挂工具）供给历史时去掉工具调用与工具结果。
///
/// 这种历史只会来自老会话：身份单向锁之前就被翻回普通角色的智能体，名下会话带着工具调用。
/// provider 本身不报错（实测 DeepSeek），但请求里完全不带工具定义时，历史若停在工具结果之后，
/// 模型会照着历史再编一个调用出来——而这一侧没有任何工具接得住它。只改发出去的那份，存档不动
/// </summary>
internal static class PromptOnlyHistory
{
    /// <summary>
    /// 去掉工具内容；整条只剩工具内容的消息一并去掉，其余消息原样保留（未改动的不复制）
    /// </summary>
    /// <param name="messages">供给的历史</param>
    /// <returns>不含工具调用与结果的历史</returns>
    public static IReadOnlyList<ChatMessage> StripToolContents(IReadOnlyList<ChatMessage> messages)
    {
        if (!messages.Any(HasToolContent)) return messages;

        List<ChatMessage> stripped = new(messages.Count);
        foreach (ChatMessage message in messages)
        {
            if (!HasToolContent(message))
            {
                stripped.Add(message);
                continue;
            }

            List<AIContent> kept = message.Contents.Where(x => !IsToolContent(x)).ToList();
            if (kept.Count == 0 || kept.All(x => x is TextContent { Text: var text } && string.IsNullOrWhiteSpace(text))) continue;

            ChatMessage copy = message.Clone();
            copy.Contents = kept;
            stripped.Add(copy);
        }

        return stripped;
    }

    private static bool HasToolContent(ChatMessage message) => message.Contents.Any(IsToolContent);

    private static bool IsToolContent(AIContent content) => content is FunctionCallContent or FunctionResultContent;
}
