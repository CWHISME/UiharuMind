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
        "Send a message to someone who works on it in their own session and replies when done.";
    /// <summary>
    /// <c>to</c> 参数说明，<b>按收件人名单装配时拼</b>。一个参数收两种收件人（人 / 聊过的那次对话），
    /// 因为对模型来说这本来就是同一个动作——给某人发消息，区别只在这个人是刚认识还是已经聊过。
    ///
    /// 名单直接列在这里而不是系统提示：名字就写在要填名字的地方，模型不必去别处对照。
    /// 没挂子角色时<b>只字不提名字</b>——实测弱模型见到「可以填名字」就自己造一个
    /// （"general"、"general-reviewer"、"reviewer"），报错后才改成留空。
    /// 名单只在装配时变，变了本来就要重新装配（系统提示同在前缀里），不会在一次装配之内改 schema（ADR 0044 决策 4、5 补注）。
    /// </summary>
    /// <param name="roster">可点名的收件人；空即没挂子角色</param>
    /// <returns>参数说明</returns>
    public static string BuildToParam(IReadOnlyList<SubAgentChoice> roster)
    {
        const string continueOrNew =
            "the id from an earlier [sub-session: …] line to continue that conversation, " +
            "or leave it empty to message someone new.";
        if (roster.Count == 0) return "Pass " + continueOrNew;

        IEnumerable<string> people = roster.Select(x =>
            x.Description.Length > 0 ? $"- {x.Name}: {x.Description}" : $"- {x.Name}");
        return "One of the names below, " + continueOrNew + "\n" + string.Join("\n", people);
    }

    /// <summary>
    /// <c>content</c> 参数说明。补一句「写具体」：压缩后
    /// <c>HistoryHandoff.BuildSubSessionRoster</c> 靠原文认出这次委派，含糊一句等于续跑时认不出。
    ///
    /// 「对方看不到你们的对话」这句是承重的：不写，模型会写出「如上所述」这种
    /// 对方根本看不懂的引用。
    /// </summary>
    public const string ContentParam =
        "Your message. They cannot see your conversation, so make it self-contained. " +
        "When continuing, this is your follow-up: a question, a correction, or 'keep going'.";

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
        "Optional short role (e.g. 'senior C# reviewer', 'devil's advocate'). " +
        "Becomes the session title and their identity, setting what they focus on and how they weigh trade-offs. " +
        "With a named person, they keep their persona and this adds the emphasis.";
    
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
        "Optional exact model name.";
}
