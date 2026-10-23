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
/// 对方能反问，回信以来信交回——描述里明说「they can ask you back in their reply」。
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
        "新开一位子代理：让某人在他自己的会话里干活，做完把回复交回来；" +
        "他可以在回复里反问。适合用于读大量材料、一段可拆分的实现、深度调查、评审或第二意见，或你卡住的时候。\n" +
        "它在后台运行：你现在先收到一张回执。" +
        "继续做任何不依赖它的活；如果剩下的都依赖它，就结束本轮——" +
        "回复落地时会把你唤醒。\n" +
        "一条回复并不关闭话题：同一主题的后续（评审新修复、复核结论、收窄范围，甚至提交之后）都用 `" +
        SubAgentTool.MessageToolName + "` 发回同一位子代理——如果中途需要补充信息、与其交流，同样可以随时通过这种方式进行对话。只有新主题才新开一位——" +
        "新开的不了解之前的对话。";

    /// <summary>
    /// <c>prompt</c> 参数说明。
    ///
    /// 「对方看不到你们的对话」这句是承重的：不写，模型会写出「如上所述」这种
    /// 对方根本看不懂的引用。压缩后 <c>HistoryHandoff.BuildSubSessionRoster</c> 也靠原文认出这次委派。
    /// </summary>
    public const string PromptParam =
        "任务或问题。他看不到你们的对话，所以要把任务写完整、自包含。";

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
        return "可选。取下列名字之一；留空则用通用子代理。\n" + string.Join("\n", people);
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
        "可选的角色名称（如「资深 C# 评审」「唱反调的人」）。" +
        "会成为会话标题和对方的身份，决定他关注什么、如何权衡取舍。" +
        "它改变的是视角，不是能力。" +
        "指定了 `subagent_type` 时，对方保留人格，这个参数补充侧重点。";
    
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
        "可选的精确模型名。";

    /// <summary>
    /// <see cref="SubAgentTool.MessageToolName"/> 的描述：给已经开过的那位续发一条，跑着时就是插话。
    /// </summary>
    public const string SendMessageDescription =
        "给已经开过的那位子代理发一条消息：后续追问、纠正，或「继续」。" +
        "如果他还在干活，会把它并入正在写的回复里；无论哪种，你都会收到一条回复。";

    /// <summary>
    /// <c>to</c> 参数说明。回执末行整行粘进来也认（<c>SubAgentTool.NormalizeTo</c>）。
    /// </summary>
    public const string ToParam =
        "来自之前 [sub-session: …] 行的 id；找不到时，报错会列出你聊过的人。";

    /// <summary><c>message</c> 参数说明</summary>
    public const string MessageParam =
        "你的消息。他记得你们之前的对话，不必重复。";

    /// <summary>
    /// 子代理侧联系工具（<see cref="Tools.ParentMessageTool.ToolName"/>）的描述：收件人固定是主代理，
    /// 是委派那条通道的反向。放在工具上而不是系统提示里——决策时刻模型正打算调的是这把
    /// </summary>
    public const string ParentMessageDescription =
        "给派活给你的主代理发一条消息：问他要信息、请他拍板，或中途汇报进展。" +
        "收件人固定是主代理；他会回话，回复会以【发信人】开头送到你这边。";

    /// <summary><c>message</c> 参数说明（子代理侧那把）</summary>
    public const string ParentMessageMessageParam =
        "你要发给主代理的话（问题、请求拍板或进度汇报）。";

    /// <summary>
    /// 子代理侧联系工具的回执。紧挨着模型的下一个 token，只留两条护栏：
    /// 发出去了、回复什么时候以什么形状来——不必为它等，先做能做的
    /// </summary>
    public const string ParentMessageReceipt =
        "已发给主代理。他会看到并回话，回复会以【发信人】开头的消息送到你这边；" +
        "在收到前先做能做的部分，需要他拍板的可以等回复到了再继续。";
}
