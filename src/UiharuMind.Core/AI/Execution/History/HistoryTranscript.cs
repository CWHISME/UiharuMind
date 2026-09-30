/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 会话历史的纯文本转录：交接时从历史整份重写，交接文档末尾给出路径，
/// 压缩之后模型要回查前情，就用现成的 <c>Grep</c>/<c>Read</c> 搜它。
///
/// 为"好搜"而设计，不为还原——历史 jsonl 一条消息一行，行首全是 JSON 字段，
/// 而 Grep 对超长命中行从行首截断，命中点稍靠后就看不到。所以这里：
/// <list type="bullet">
/// <item>每条消息一个短标题行 <c>## #N 角色 时间</c>，N 与 <c>.history.jsonl</c> 的行号一致；</item>
/// <item>正文按原换行拆行，超长段落再折行，每行都落在 Grep 的单行截断之内；</item>
/// <item>工具调用与结果只留一行摘要，推理内容不收（体量大，对回查没用）。</item>
/// </list>
/// jsonl 仍是唯一真源，这份只是派生视图。
/// </summary>
internal static class HistoryTranscript
{
    /// <summary>折行宽度，须低于 Grep 的单行截断（<see cref="GrepResultShaper.MaxLineChars"/>）</summary>
    internal const int WrapChars = 400;

    private const int BreakSearchChars = 80; //折行时往回找断点的范围
    private const int SummaryChars = 200; //工具调用/结果摘要的篇幅
    private static readonly char[] BreakChars = " \t，。；：！？、,.;:!?)）】」".ToCharArray();

    /// <summary>
    /// 只有 <c>Grep</c> 与 <c>Read</c> 都在场时转录才有意义：给一个搜不了的路径，等于指一件做不了的事
    /// </summary>
    /// <param name="tools">会话实际装配的工具</param>
    /// <returns>能搜转录返回 true</returns>
    public static bool IsSearchableWith(IEnumerable<AITool>? tools)
    {
        if (tools == null) return false;
        List<string> names = tools.Select(tool => tool.Name).ToList();
        return names.Contains(FileToolNames.Grep) && names.Contains(FileToolNames.Read);
    }

    /// <summary>
    /// 渲染并覆盖写入会话的转录
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="tools">会话实际装配的工具</param>
    /// <returns>转录文件的绝对路径；不该写（临时会话、搜不了）或写盘失败为 null</returns>
    public static string? TrySave(ChatSession session, IEnumerable<AITool>? tools)
    {
        // 临时会话不落盘,也不走删除流程,写了转录就是一个永远没人删的孤儿文件
        if (session.IsTransient || !IsSearchableWith(tools)) return null;

        try
        {
            return SessionManager.Instance.SaveTranscript(session.SessionId, Render(session.History));
        }
        catch (Exception e)
        {
            Log.Warning($"Save transcript failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 渲染转录正文
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <returns>转录正文</returns>
    public static string Render(IReadOnlyList<ChatMessage> history)
    {
        StringBuilder sb = new();
        for (int i = 0; i < history.Count; i++)
        {
            ChatMessage message = history[i];
            sb.Append("## #").Append(i + 1).Append(' ').Append(Label(message));
            if (message.CreatedAt is { } createdAt) sb.Append(' ').Append(createdAt.ToString("yyyy-MM-dd HH:mm"));
            sb.Append('\n');

            foreach (string line in BodyLines(message)) AppendWrapped(sb, line);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    // 注入类的 user 消息标出来源:回查"用户说过什么"时要分得清谁说的
    private static string Label(ChatMessage message)
    {
        string role = message.Role.Value;
        if (Has(message, ChatMessageAnnotations.Handoff)) return "handoff";
        if (Has(message, ChatMessageAnnotations.SubAgentReport)) return $"{role} (sub-agent report)";
        if (Has(message, ChatMessageAnnotations.ParentInterjection)) return $"{role} (from dispatcher)";
        if (Has(message, ChatMessageAnnotations.GroupDelivery)) return $"{role} (group)";
        if (Has(message, ChatMessageAnnotations.Knowledge)) return $"{role} (knowledge)";
        if (Has(message, ChatMessageAnnotations.NamedSkill)) return $"{role} (skill)";
        return role;
    }

    private static IEnumerable<string> BodyLines(ChatMessage message)
    {
        // 点名技能的正文是整份技能说明,不是用户的话;用户敲的那一行另存在标记里
        if (message.AdditionalProperties?.TryGetValue(ChatMessageAnnotations.NamedSkillInput, out object? input) == true)
        {
            yield return input?.ToString() ?? string.Empty;
            yield break;
        }

        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextReasoningContent:
                    break;
                case TextContent text:
                    foreach (string line in text.Text.Split('\n')) yield return line.TrimEnd('\r');
                    break;
                case FunctionCallContent call:
                    string arguments = call.Arguments == null
                        ? string.Empty
                        : string.Join(", ", call.Arguments.Select(x => $"{x.Key}={x.Value}"));
                    yield return $"[tool call] {call.Name}({Summarize(arguments)})";
                    break;
                case FunctionResultContent result:
                    string output = result.Result?.ToString() ?? string.Empty;
                    yield return $"[tool result] {Summarize(output)} ({output.Length} chars)";
                    break;
                case DataContent data:
                    yield return $"[attachment {data.MediaType}]";
                    break;
                case UriContent uri:
                    yield return $"[attachment {uri.Uri}]";
                    break;
                case ErrorContent error:
                    yield return $"[error] {Summarize(error.Message)}";
                    break;
            }
        }
    }

    private static bool Has(ChatMessage message, string annotation) =>
        message.AdditionalProperties?.ContainsKey(annotation) == true;

    private static string Summarize(string text)
    {
        string oneLine = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= SummaryChars ? oneLine : oneLine[..SummaryChars] + "…";
    }

    // 优先断在空白或标点后,找不到才硬断;硬断不劈开代理对
    private static void AppendWrapped(StringBuilder sb, string line)
    {
        int start = 0;
        while (line.Length - start > WrapChars)
        {
            int end = start + WrapChars;
            int breakAt = line.LastIndexOfAny(BreakChars, end - 1, BreakSearchChars);
            end = breakAt > start ? breakAt + 1 : end;
            if (char.IsHighSurrogate(line[end - 1])) end--;

            sb.Append(line, start, end - start).Append('\n');
            start = end;
        }

        sb.Append(line, start, line.Length - start).Append('\n');
    }
}
