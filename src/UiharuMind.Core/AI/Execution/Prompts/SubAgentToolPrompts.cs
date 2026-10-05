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
/// 委派两把工具的「模型可见」说明书：工具描述与参数说明，唯一出处。
///
/// <see cref="SubAgentTool.ToolName"/> 新开、<see cref="SubAgentTool.MessageToolName"/> 续聊（ADR 0065）。
/// 委派没有系统提示里的纪律段（ADR 0054 补注）：什么时候新开、什么时候续聊，写在模型做那个决定的
/// 两把工具上，只此一处。
///
/// 名字对齐 Claude Code（模型训练里见惯的写法），语义保留 ADR 0044 的对话心智：
/// 对方能反问，回信以来信交回——描述里明说「they can ask you back」。
/// </summary>
public static class SubAgentToolPrompts
{
    /// <summary>
    /// <see cref="SubAgentTool.ToolName"/> 的描述：是什么、什么时候值得开、后台意味着什么、什么时候<b>不</b>该新开。
    ///
    /// ⚠️ <b>与回执分工</b>：这里说政策（依赖它的事要等）；「这一次尚无结果」那条护栏归回执
    /// （<c>BackgroundSubAgentDispatcher.Dispatch</c>），它紧挨着误读发生的那一刻。两边都写整段就是
    /// 固定开销与每次委派各付一遍钱。
    ///
    /// 「同一主题回到同一个人」写在这里而不是 <see cref="SubAgentTool.MessageToolName"/> 上：
    /// 该续不续的那一刻，模型正打算调的是这一把。
    ///
    /// <b>刻意不数工具</b>：对方的工具集随能力配置变，写死数量迟早与装配事实矛盾。
    /// </summary>
    public const string AgentDescription =
        "Launch a sub-agent: someone who works on it in their own session and replies when done; " +
        "they can ask you back. Use it for reading lots of material, a separable piece of implementation, " +
        "a deep investigation, a review or second opinion, or when you're stuck.\n" +
        "It runs in the background: you get a receipt now and are woken when the reply arrives. " +
        "Meanwhile do what doesn't depend on it; wait for what does.\n" +
        "A reply doesn't close the topic: follow-ups on the same subject (reviewing a new fix, " +
        "re-checking a conclusion, a narrower scope, even after a commit) go to the same sub-agent with `" +
        SubAgentTool.MessageToolName + "`. Launch a new one only for a new subject — " +
        "a new one knows nothing of earlier conversations.";

    /// <summary>
    /// <c>prompt</c> 参数说明。
    ///
    /// 「对方看不到你们的对话」这句是承重的：不写，模型会写出「如上所述」这种
    /// 对方根本看不懂的引用。压缩后 <c>HistoryHandoff.BuildSubSessionRoster</c> 也靠原文认出这次委派。
    /// </summary>
    public const string PromptParam =
        "The task or question. They cannot see your conversation, so make it self-contained.";

    /// <summary>
    /// <c>subagent_type</c> 参数说明，<b>按名单装配时拼</b>；只在挂了子角色时才有这个参数。
    /// 名字就写在要填名字的地方，模型不必去别处对照。
    /// </summary>
    /// <param name="roster">可点名的子角色，非空</param>
    /// <returns>参数说明</returns>
    public static string BuildSubagentTypeParam(IReadOnlyList<SubAgentChoice> roster)
    {
        IEnumerable<string> people = roster.Select(x =>
            x.Description.Length > 0 ? $"- {x.Name}: {x.Description}" : $"- {x.Name}");
        return "Optional. One of the names below; leave empty for a general sub-agent.\n" + string.Join("\n", people);
    }

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
        "It changes perspective, not abilities. " +
        "With a `subagent_type`, they keep their persona and this adds the emphasis.";
    
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

    /// <summary>
    /// <see cref="SubAgentTool.MessageToolName"/> 的描述：给已经开过的那位续发一条，跑着时就是插话。
    /// </summary>
    public const string SendMessageDescription =
        "Send a message to a sub-agent you already launched: a follow-up, a correction, or 'keep going'. " +
        "If they're still working, it reaches them before their next step.";

    /// <summary>
    /// <c>to</c> 参数说明。回执末行整行粘进来也认（<c>SubAgentTool.NormalizeTo</c>）。
    /// </summary>
    public const string ToParam =
        "The id from an earlier [sub-session: …] line.";

    /// <summary><c>message</c> 参数说明</summary>
    public const string MessageParam =
        "Your message. They remember your earlier exchange, so no need to repeat it.";
}
