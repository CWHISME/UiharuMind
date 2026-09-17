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
        "It can change files, run commands — same permissions as you.";

    /// <summary>只读探索工具描述</summary>
    public const string RunReadOnlyAgentDescription =
        "Run a read-only agent for fact-finding: survey files, search code, research the web. " +
        "It has only read-only tools and cannot change anything.";

    /// <summary>派活工具 task 参数说明</summary>
    public const string TaskParam =
        "What the agent should do: what to find out, over what scope, and what the final report should contain.";

    /// <summary>派活工具 agent 参数说明（点名花名册子智能体）</summary>
    public const string AgentParam = "Which mounted agent to run. Omit it for the default agent.";

    /// <summary>
    /// 派活工具 role 参数说明（身份/职业）。
    ///
    /// 从前末尾还有一句 "Ignored when `agent` names a mounted agent." ——<b>那是假的</b>：
    /// <c>SubAgentTool.Launch</c> 无条件 <c>NormalizeRole</c> 并钉在子会话上，装配侧也无条件
    /// 输出身份句。点名一个角色卡、再给它本次侧重，本就是有意义的组合，不该丢；
    /// 该修的是那句话，不是那个行为。两者同时在场时谁压谁，由提示词明确表态
    /// （见 <c>SubAgentPrompts.RoleOverPersona</c>）。
    /// </summary>
    public const string RoleParam =
        "Optional short role for the agent (e.g. 'senior C# reviewer' or a name). " +
        "Used as the session title and injected into its identity. " +
        "It sets what the agent attends to and how it judges trade-offs, not what it is capable of. " +
        "Works together with `agent`: the named agent keeps its own persona, " +
        "and this role is the emphasis for this run.";

    /// <summary>
    /// 派活工具 model 参数说明。
    ///
    /// <b>刻意不给模型清单</b>（既不拼进工具描述，也不另开一个 ListModels 工具，见 ADR 0031）：
    /// 常规路由由设置页那两档默认解决，而"这一趟该换个模型"的判断依据
    /// （哪个强、哪个便宜、哪个上下文长）本就不在一串名字里。名字由用户投喂——
    /// 会话页模型下拉上有复制按钮。
    ///
    /// 名字错了不会炸：解析不到就回退默认，并在回执里说一声。
    /// </summary>
    public const string ModelParam =
        "Optional exact model name to use for this run (as shown in the app's model list). " +
        "Omit it to use the configured default. ";
    
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
