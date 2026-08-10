/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 交接文档式压缩：上下文快满时，让<b>当前模型</b>把这段对话写成一份交接文档
/// （已完成什么、正在做什么、进度到哪、下一步），之后喂给模型的历史就从这份文档开始。
///
/// 与框架自带的 <c>SummarizationCompactionStrategy</c> 的两个关键差别：
/// <list type="number">
/// <item>框架那套是<b>在环</b>的——每轮请求前对消息集重跑一遍。而我们的历史供给每轮都是全量，
/// 于是它每轮都要重新总结一次，等于每轮多一次模型调用。这里改为<b>总结一次、落进历史</b>，
/// 之后每轮直接从那条消息开始供给，不再重算。</item>
/// <item>用的是当前模型自己，不是另配一个摘要模型。框架文档为那套标了间接提示词注入的风险
/// （摘要输出会永久替换原始消息，因此摘要模型必须与主模型同等可信）——同一个模型就没有这个落差。</item>
/// </list>
///
/// 框架的截断仍然挂着当兜底：写交接文档本身也要发一次请求，它失败时得有东西接住。
/// </summary>
public static class HistoryHandoff
{
    /// <summary>触发水位（占输入预算的比例）。低于框架截断的水位，好让截断几乎轮不到。</summary>
    public const double Threshold = 0.75;

    /// <summary>交接文档在对话里的标题，界面与提示词共用一处</summary>
    public const string Title = "Context handoff";

    private const string Instruction = """
        You are about to lose access to the earlier part of this conversation: it is being
        compacted to fit the context window. Write a handoff document for your future self.

        Cover, in this order, and omit any section that genuinely has nothing to report:
        1. What the user is ultimately trying to accomplish.
        2. What has already been done and decided, including the reasoning behind each decision.
        3. What is in progress right now and exactly how far it got.
        4. What comes next, and anything still unresolved or blocked.
        5. Concrete details that would be expensive to rediscover: file paths, identifiers,
           commands, numbers, names, API shapes.

        Rules:
        - Write it for someone who will see ONLY this document and nothing before it.
        - Be specific. "Fixed the bug" is useless; name the file and what changed.
        - Do not invent anything that did not happen in the conversation.
        - Do not address the user, do not ask questions, do not offer to continue.
        - Write in the language the conversation is in.
        - Output the document only. No preamble, no closing remarks.
        """;

    /// <summary>
    /// 是否该写交接文档了
    /// </summary>
    /// <param name="lastInput">最近一次响应的输入 token（服务端给的真实占用）</param>
    /// <param name="contextLength">当前模型的上下文上限</param>
    /// <returns>达到水位为 true</returns>
    public static bool ShouldWrite(long lastInput, int contextLength)
    {
        int budget = HistoryCompaction.InputBudgetFor(contextLength);
        return budget > 0 && lastInput > budget * Threshold;
    }

    /// <summary>
    /// 构造交接文档消息。
    ///
    /// 角色取 <b>system</b> 而不是 assistant，是为了不被框架截断吃掉：交接文档在供给窗口里
    /// 恰好是最老的一条，而 <c>TruncationCompactionStrategy</c> 正是从最老的非 system 组开始删——
    /// 取 assistant 的话，占用一旦冲到截断水位，第一个被删的就是它自己，那段历史就白压了。
    /// 框架对 system 组一律保留，这条因此稳住。
    /// </summary>
    /// <param name="note">文档正文</param>
    /// <returns>可直接写进历史的消息</returns>
    public static ChatMessage CreateNote(string note)
    {
        return new ChatMessage(ChatRole.System, $"[{Title}]\n{note}")
        {
            CreatedAt = DateTimeOffset.Now,
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Handoff] = Title },
        };
    }

    /// <summary>
    /// 是否为交接文档消息
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是则 true</returns>
    public static bool IsNote(ChatMessage message)
    {
        return message.AdditionalProperties?.ContainsKey(ChatMessageAnnotations.Handoff) == true;
    }

    /// <summary>
    /// 取交接文档的正文（去掉给模型认的那行标题，界面上已有标题了）
    /// </summary>
    /// <param name="text">消息文本</param>
    /// <returns>正文</returns>
    public static string NoteBody(string text)
    {
        string prefix = $"[{Title}]";
        return text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..].TrimStart() : text;
    }

    /// <summary>
    /// 取喂给模型的历史区间起点：最后一条交接文档所在的下标。
    /// 文档本身要包含在内——它就是模型能看到的全部前情。
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <returns>起点下标；没有交接文档时为 0</returns>
    public static int SupplyStartIndex(IReadOnlyList<ChatMessage> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (IsNote(history[i])) return i;
        }

        return 0;
    }

    /// <summary>
    /// 让模型写一份交接文档
    /// </summary>
    /// <param name="client">模型客户端</param>
    /// <param name="history">要交接的历史（通常是当前供给给模型的那一份）</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>文档正文；失败或产出为空时返回 null</returns>
    public static async Task<string?> WriteAsync(IChatClient client, IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        if (history.Count == 0) return null;

        List<ChatMessage> messages = new(history.Count + 1);
        messages.AddRange(history);
        messages.Add(new ChatMessage(ChatRole.User, Instruction));

        try
        {
            // 不带工具:这是一次纯文本产出,让它去调工具只会跑偏并多烧配额
            ChatResponse response = await client
                .GetResponseAsync(messages, new ChatOptions(), cancellationToken)
                .ConfigureAwait(false);
            string text = response.Text.Trim();
            return text.Length == 0 ? null : text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warning($"Write context handoff failed: {e.Message}");
            return null;
        }
    }
}
