/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Prompts;

/// <summary>
/// 委派工具的「模型可见」说明书：工具描述与参数说明，唯一出处。
///
/// 只收 <see cref="SubAgentTool.ToolName"/> 这一把工具；其他工具的描述仍跟随各自的 Tool 类。
/// 什么时候该委派，判据在 <see cref="AgentToolPrompts.BuildDelegation(string)"/>，这里不重复；
/// 描述只留「是什么 + 副作用/限制 + 参数怎么填」。
///
/// <b>措辞是这层的本体</b>（ADR 0044）：从前三把工具叫 RunAgent / RunReadOnlyAgent /
/// ContinueAgent，签名本身在教模型「agent 是可运行的东西、task 是输入、产出是返回值」——
/// 函数调用心智。用了它，模型就把对方当工具人：派活 → 等结果 → 自己总结，
/// 对方没有机会反问、没有机会说「你这个需求我没听懂」。
/// 所以这里一律写成<b>给某个人发消息</b>，不写 run / task / execute。
/// </summary>
public static class SubAgentToolPrompts
{
    /// <summary>
    /// 委派工具描述。<b>只讲能力边界</b>：什么时候该发唯一收在
    /// <see cref="AgentToolPrompts.BuildDelegation(string)"/>，这里不重复整段政策。
    ///
    /// <b>刻意不数工具</b>：对方的工具集随能力配置变，写死数量迟早与装配事实矛盾。
    /// <b>也刻意不提「只读」那一档</b>：档位差异已经退役，对方能做什么由权限档说了算。
    /// </summary>
    public const string SendMessageDescription =
        "Send a message to someone and let them work on it. " +
        "They have the same permissions as you — they can change files and run commands. " +
        "They can also come back with a question instead of an answer. " +
        "Reply is not immediate: you get a receipt now and their message arrives later.";

    /// <summary>
    /// <c>to</c> 参数说明。一个参数收两种收件人（人 / 一次进行中的委派），
    /// 因为对模型来说这本来就是同一个动作——**给某人发消息**，
    /// 区别只在这个人是刚认识还是已经聊过。查找顺序见 <c>SubAgentTool.Resolve</c>。
    ///
    /// 名单不写进这里（那会让工具定义随成员增减而变，失效前缀缓存，见 ADR 0044 决策 4、5），
    /// 可找的人列在系统提示的委派一节里。
    /// </summary>
    public const string ToParam =
        "Who to send it to: a name from the people listed in your instructions, " +
        "or the id inside the [sub-session: …] line from an earlier receipt to continue that conversation. " +
        "Leave it empty to reach the default helper.";

    /// <summary>
    /// <c>content</c> 参数说明。补一句「写具体」：压缩后
    /// <c>HistoryHandoff.BuildSubSessionRoster</c> 靠原文认出这次委派，含糊一句等于续跑时认不出。
    ///
    /// 「对方看不到你们的对话」这句是承重的：不写，模型会写出「如上所述」这种
    /// 对方根本看不懂的引用。
    /// </summary>
    public const string ContentParam =
        "What you want to say to them. " +
        "They cannot see your conversation — say enough that it stands on its own. " +
        "When continuing an earlier one, this is your follow-up: a question, a correction, or just keep going.";

    /// <summary>
    /// <c>role</c> 参数说明（身份/职业）。
    ///
    /// 从前末尾还有一句 "Ignored when `agent` names a mounted agent." ——<b>那是假的</b>：
    /// <c>SubAgentTool.Launch</c> 无条件 <c>NormalizeRole</c> 并钉在子会话上，装配侧也无条件
    /// 输出身份句。点名一个角色卡、再给它本次侧重，本就是有意义的组合，不该丢；
    /// 该修的是那句话，不是那个行为。两者同时在场时谁压谁，由提示词明确表态
    /// （见 <c>SubAgentPrompts.RoleOverPersona</c>）。
    /// </summary>
    public const string RoleParam =
        "Optional short role for them (e.g. 'senior C# reviewer', 'devil's advocate', or a name). " +
        "Used as the session title and injected into their identity. " +
        "It sets what they attend to and how they judge trade-offs. " +
        "Works together with a named person: they keep their own persona, " +
        "and this role is the emphasis for this one. " +
        "Ignored when continuing an earlier conversation.";

    /// <summary>
    /// <c>model</c> 参数说明。
    ///
    /// <b>刻意不给模型清单</b>（既不拼进工具描述，也不另开一个 ListModels 工具，见 ADR 0031）：
    /// 常规路由由设置页的默认解决，而"这一趟该换个模型"的判断依据
    /// （哪个强、哪个便宜、哪个上下文长）本就不在一串名字里。名字由用户投喂——
    /// 会话页模型下拉上有复制按钮。
    ///
    /// 名字错了不会炸：解析不到就回退默认，并在回执里说一声。
    /// </summary>
    public const string ModelParam =
        "Optional exact model name for this one. Ignored when continuing an earlier conversation.";

    /// <summary>
    /// 收件人名单的小标题。<b>这段不再进工具描述</b>，改由装配侧拼进系统提示的委派一节
    /// （ADR 0044 决策 4）：名单进 schema 会让工具定义随成员增减而变，前缀缓存全废。
    /// </summary>
    public const string RosterHeading =
        "People you can send to (pass the name as `to`, or leave it empty for the default helper):";
}
