/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 判扩展调用风险时能拿到的外部事实（ADR 0079）。
///
/// 分级仍只在 <see cref="ExtensionRisk"/> 一处定义，这里只回答它自己回答不了的三件事：
/// MCP 工具是不是自己声明了只读；某个绝对路径算不算本会话界内；此刻有没有人在场。
/// 界内判据<b>必须与审批放行同一口径</b>：浏览器打开自己工作区里的页面（拾取器、报告）与「界内写入」
/// 是同一件事，两侧判据一旦各写一份，就会出现「规则放行了、卡上却写着每次都问」。
/// </summary>
public sealed record ExtensionRiskContext
{
    /// <summary>什么都不认的上下文（测试与老调用点用）</summary>
    public static readonly ExtensionRiskContext Empty = new();

    /// <summary>MCP 工具是否自己声明了只读（server 名, 工具名）</summary>
    public Func<string, string, bool>? McpReadOnly { get; init; }

    /// <summary>这个绝对路径是否落在本会话界内（工作区 / 草稿房间 / 登记且同工作区的 worktree）</summary>
    public Func<string, bool>? IsPathInScope { get; init; }

    /// <summary>
    /// 此刻没人在场：定时运行、被定时运行派出，或所在的群在离席。
    /// <b>动态求值</b>（离席可能在一轮中途开始或结束）：<see cref="EExtensionRisk.SessionWideOnly"/> 在
    /// 这一刻一律拒——「读遍本机」的同意得是人亲手勾的，无人时可没人去勾
    /// </summary>
    public Func<bool>? IsUnattended { get; init; }
}
