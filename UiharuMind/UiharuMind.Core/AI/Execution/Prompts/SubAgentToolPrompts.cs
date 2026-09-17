/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Prompts;

/// <summary>
/// 子代理委派相关工具的「模型可见」说明书：工具描述与参数说明，唯一出处。
///
/// 只收委派那一组工具（RunAgent / RunReadOnlyAgent / ContinueAgent），
/// 其他工具的描述仍跟随各自的 Tool 类。选哪一档的判据在
/// <see cref="AgentToolPrompts.SubAgentDefault"/>，这里不重复；
/// 描述只留「是什么 + 副作用/限制 + 参数怎么填」。
/// </summary>
public static class SubAgentToolPrompts
{
    /// <summary>通用委派工具描述</summary>
    public const string RunAgentDescription =
        "Run an agent on a task and get a focused report. " +
        "It can change files, run commands, use MCP tools — same permissions as you.";

    /// <summary>只读探索工具描述</summary>
    public const string RunReadOnlyAgentDescription =
        "Run a read-only agent for fact-finding: survey files, search code, research the web. " +
        "It has only read-only tools and cannot change anything.";

    /// <summary>派活工具 task 参数说明</summary>
    public const string TaskParam =
        "What the agent should do: what to find out, over what scope, and what the final report should contain.";

    /// <summary>派活工具 agent 参数说明（点名花名册子智能体）</summary>
    public const string AgentParam = "Which mounted agent to run. Omit it for the default agent.";

    /// <summary>派活工具 role 参数说明（身份/职业）</summary>
    public const string RoleParam =
        "Optional short role for the agent (e.g. 'senior C# reviewer' or a name). " +
        "Used as the session title and injected into its identity. " +
        "Ignored when `agent` names a mounted agent.";

    /// <summary>花名册提示（追加在工具描述之后）</summary>
    public const string RosterHeading =
        "Named agents you can run (pass one as `agent`, or omit it for the default agent):";

    /// <summary>追问/续跑工具的 subSession 参数说明</summary>
    public const string ContinueSubSessionParam =
        "A run id: the `[sub-session: …]` marker from a previous receipt.";

    /// <summary>追问/续跑工具的 message 参数说明</summary>
    public const string ContinueMessageParam =
        "What to tell the agent next: a follow-up question, a correction, or to keep going.";

    /// <summary>追问/续跑工具描述</summary>
    public const string ContinueDescription =
        "Send another message to an agent you already ran (same session — " +
        "it keeps everything done so far) and get an updated report.";
}
