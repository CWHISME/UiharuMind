/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 一位子代理的「身份行」：主题/标题 + 短号，附点名与只读档。
///
/// 同一句话在子代理列表（<c>ChatSessionTools.DescribeSub</c>）、记忆压缩的委派段
/// （<c>HistoryHandoff.BuildSubSessionRoster</c>）、回信信头（<c>SubAgentReportHandoff</c>）
/// 里各写了一遍，三个地方拼错一个字段就是三个版本漂移。统一收在这里，
/// 调用方只在自己的行尾追加状态/上下文这类<b>当下</b>才有的信息。
/// </summary>
public static class SubAgentDescriptor
{
    /// <summary>
    /// 按索引元数据描述一位子代理
    /// </summary>
    /// <param name="meta">子会话的索引元数据</param>
    /// <returns>身份行，形如 `- 《主题》[短号] · 点名：X · 只读`</returns>
    public static string Describe(ChatSessionMeta meta) =>
        Describe(meta.Title, meta.SubAgentName, meta.SubAgentType, meta.SessionId);

    /// <summary>
    /// 按子会话本体描述一位子代理（装配与交回路径手里是它，字段同 <see cref="ChatSessionMeta"/>）
    /// </summary>
    /// <param name="session">子会话本体</param>
    /// <returns>身份行，与 <see cref="Describe(ChatSessionMeta)"/> 同一口径</returns>
    public static string Describe(ChatSession session) =>
        Describe(session.Title, session.SubAgentName, session.SubAgentType, session.SessionId);

    /// <summary>
    /// 上下文占用（<c>12k/32k · 模型名</c>）：主代理判断这位还剩多少、要不要换人接着干。
    /// 只给占用没有窗口说明不了问题，所以窗口与模型名跟着一起给。从没跑过为空串，调用方整段省略
    /// </summary>
    /// <param name="session">子会话本体：占用、窗口与模型名都精确</param>
    /// <returns>占用行；没有记录为空串</returns>
    public static string DescribeUsage(ChatSession session) =>
        DescribeUsage(session.LastInputTokens, session.ChatModelRunningData?.ContextLength ?? 0,
            session.ChatModelRunningData?.ModelName);

    /// <summary>
    /// 上下文占用。冷会话（只有索引元数据）窗口与模型名不猜，与界面「上限未知只给占用」同一口径
    /// </summary>
    /// <param name="meta">子会话的索引元数据</param>
    /// <param name="body">已驻留的本体；没有为 null</param>
    /// <returns>占用行；没有记录为空串</returns>
    public static string DescribeUsage(ChatSessionMeta meta, ChatSession? body) =>
        DescribeUsage(body != null ? body.LastInputTokens : meta.LastInputTokens,
            body?.ChatModelRunningData?.ContextLength ?? 0, body?.ChatModelRunningData?.ModelName);

    private static string DescribeUsage(long usage, int contextLength, string? modelName)
    {
        if (usage <= 0) return string.Empty;

        // 「8k/1M · 模型名」：分隔符只用中点，不再套括号——这一行会挂在回信附言末尾，
        // 括号叠着方括号读起来像机器码
        string line = contextLength > 0
            ? $"{TurnUsageLedger.Format(usage)}/{TurnUsageLedger.Format(contextLength)}"
            : TurnUsageLedger.Format(usage);
        return string.IsNullOrEmpty(modelName) ? line : $"{line} · {modelName}";
    }

    private static string Describe(string title, string name, ESubAgentType type, string sessionId)
    {
        StringBuilder sb = new(title.Length > 0
            ? $"- {title}[{SubSessionIdAlias.Short(sessionId)}]"
            : $"- [{SubSessionIdAlias.Short(sessionId)}]");
        if (name.Length > 0) sb.Append(" · 点名：").Append(name);
        // 探索档恒定只读（CreateAgent 置 readOnly 派出）：主代理续用前要知道这位改不了东西
        if (type == ESubAgentType.Explorer) sb.Append(" · 只读");
        return sb.ToString();
    }
}
