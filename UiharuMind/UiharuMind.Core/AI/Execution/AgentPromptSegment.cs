/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 系统提示里的一段。装配时按<b>拼接现场</b>登记，不靠事后按 markdown 标题反切——
/// 标题一改，反切法就静默错，而它错的方式是数字看着还挺合理。
/// </summary>
public enum EPromptSection
{
    /// <summary>角色段（人格 + 用户卡 + 对话模板）</summary>
    Character,

    /// <summary>工具纪律段（含工作目录那一小节）</summary>
    ToolDisciplines,

    /// <summary>MCP server 自述</summary>
    Mcp,

    /// <summary>工作区规矩（项目的 AGENTS.md / CLAUDE.md）</summary>
    Workspace,
}

/// <summary>
/// 系统提示里的一段正文及其估算占用。
///
/// <b>只存正文，不在装配时分词</b>：装配在发消息路径上，不该为一个只有面板才看的数字
/// 付整份 AGENTS.md 全文分词的钱。分词由 <see cref="AgentCapabilitySnapshot.Capture"/> 统一做，
/// 那一步本就跑在后台线程上。
/// </summary>
/// <param name="Section">段别</param>
/// <param name="Text">该段发出去的原文</param>
public sealed record AgentPromptSegment(EPromptSection Section, string Text)
{
    /// <summary>估算 token；装配时为 0，切快照时填</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// 是否计入固定开销合计。MCP 自述恒为 <c>false</c>——它已经在 MCP 那一档里计过一次，
    /// 再计一遍就是双计。它仍然登记在册，因为「模型现在到底看得见什么」要求它出现在提示词明细里
    /// </summary>
    public bool CountsTowardTotal => Section != EPromptSection.Mcp;
}
