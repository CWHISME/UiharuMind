/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Linq;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 记忆压缩式上下文整理：上下文快满时，让<b>当前模型</b>把这段对话整理成一份记忆压缩
/// （已完成什么、正在做什么、进度到哪、下一步），之后喂给模型的历史就从这份压缩开始。
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
/// 框架的截断仍然挂着当兜底：写记忆压缩本身也要发一次请求，它失败时得有东西接住。
/// </summary>
public static class HistoryHandoff
{
    /// <summary>触发水位（占输入预算的比例）。低于框架截断的水位，好让截断几乎轮不到。</summary>
    public const double Threshold = 0.8;

    /// <summary>记忆压缩在对话里的标题，界面与提示词共用一处</summary>
    public const string Title = "记忆压缩";

    /// <summary>记忆压缩正文的固定开场句（代码注入，不靠模型写）</summary>
    private const string Opening = "我来整理这段历史记忆。";

    /// <summary>「上一轮最后一句」小标题（代码注入，紧跟在模型正文之后）</summary>
    private const string LastSentenceHeading = "上一轮我的最后一句：";

    /// <summary>
    /// 附加段的分隔标题：它之后是代码现算的内容（委派清单、用户原话、转录路径），不是模型写的正文。
    /// 与正文同在一条消息里而不另起一条：另起一条的话，下次压缩要么把它一并交给模型（照样被总结），
    /// 要么剔掉它（请求从那里岔开，前缀缓存整段作废）；指令里一句"不要照抄"就够了。界面要折叠附录时按它切
    /// </summary>
    public const string AppendixHeading = "--- 以下由程序自动生成（每次压缩重新生成） ---";

    private const double NoteBudgetRatio = 0.1; //记忆压缩自己占输入预算的比例
    private const int MinNoteTokens = 400;
    private const int MaxNoteTokens = 8000;
    private const int RecallUserMessages = 15; //回忆段最多带几条用户原话;逐条原文不截断

    // 这些 user 消息不是用户本人说的话:派活方插话、群里别人的发言、知识库片段、目标模式的系统注入。
    // 交回的报告与子代理中途来信不列在这里——它们由 IsNotUserWords 在数组之前就先挡掉(UserWords 首判)
    private static readonly string[] NotUserWords =
    [
        ChatMessageAnnotations.ParentInterjection,
        ChatMessageAnnotations.GroupDelivery,
        ChatMessageAnnotations.Knowledge,
        ChatMessageAnnotations.GoalModeInjection,
    ];

    private const int CharsPerToken = 2; //中英混排的保守估计:英文约 4、中文约 1.5,取 2 不至于让模型写超

    /// <summary>
    /// 记忆压缩自己的篇幅上限（字符数）。
    ///
    /// 必须给，而且必须算出来告诉模型：文档写完就成了下一段历史的**起点**，
    /// 它自己太长的话，压缩等于没做——极端情况下一写完就又过了水位，下一轮再压一次，
    /// 陷入「压缩 → 还是满 → 再压缩」的循环。
    /// </summary>
    /// <param name="contextLength">当前模型的上下文上限</param>
    /// <returns>建议的最大字符数</returns>
    public static int NoteCharLimitFor(int contextLength)
    {
        int budget = HistoryCompaction.InputBudgetFor(contextLength);
        int tokens = Math.Clamp((int)(budget * NoteBudgetRatio), MinNoteTokens, MaxNoteTokens);
        return tokens * CharsPerToken;
    }

    private const string Instruction = 
        $$"""
        为了适配上下文窗口，这段对话更早的部分将被压缩，我即将失去直接访问它们的能力。请以「我」的视角，为未来的我整理一份压缩记忆。

        按顺序覆盖以下内容，确实没有可报的部分可以省略：
        1. 用户最终想达成什么。
        2. 已经完成和已经定下的事，包括每个决定背后的理由。
        3. 现在正在做什么、进行到了哪一步。
        4. 接下来是什么，以及任何仍未解决或卡住的事。
        5. 重新发掘起来代价很高的具体细节：文件路径、标识符、命令、数字、名字、API 形状。

        规则：
        - 这份文档是写给一个只能看到它、看不到它之前任何内容的自己。
        - 要具体。「修好了 bug」没用；写出文件名和改了什么。
        - 不要编造对话里没发生过的事。
        - 不要对用户说话，不要提问，不要主动表示继续。
        - 用对话所用的语言写。
        - 只输出文档本身。不要前言，不要收尾客套。
        - 不要调用任何工具：需要的东西都已在上面。
        - 开头那句开场白与末尾「上一轮我的最后一句」两段由代码自动加上，不要自己写。
        - 如果早先的记忆压缩以「{{AppendixHeading}}」结尾，不要照抄：它会自动重新生成。
        - 硬性上限：最多 {0} 字。这份文档会替换上面的对话，太长就违背了它的目的。把预算花在第 3 和第 5 节（当前进度与具体细节）上；第 1、2 节尽量压缩。
        """;

    /// <summary>
    /// 是否该写记忆压缩了
    /// </summary>
    /// <param name="rawInput">原始占用（有效占用加上被折叠或截断压掉的那截，见 <c>TurnUsageLedger.RawInput</c>）</param>
    /// <param name="contextLength">当前模型的上下文上限</param>
    /// <returns>达到水位为 true</returns>
    public static bool ShouldWrite(long rawInput, int contextLength)
    {
        int budget = HistoryCompaction.InputBudgetFor(contextLength);
        return budget > 0 && rawInput > budget * Threshold;
    }

    /// <summary>
    /// 这个会话派出过的委派数量、仍在跑的那几位、以及查询入口，<b>确定性追加</b>在记忆压缩正文之后。
    ///
    /// 不逐条列出：<c>ExCall</c> 调 <c>chatsession</c> 扩展的 <c>list</c> 命令能实时列出本会话派出的子代理及其状态，
    /// 比压缩时固化的快照更新、也更省篇幅。压缩里只报数量、把还在跑的点名出来（压缩后模型要知道还有哪些活挂着），
    /// 再指路怎么查、怎么续。续聊仍要说明为什么别新开：新开一位会丢掉子代理攒下的上下文。
    /// </summary>
    /// <param name="subSessions">该会话派出过的子会话</param>
    /// <param name="tools">会话装配的工具；没有扩展元工具（<c>ExCall</c>）时只报数量与运行中列表，不给查不了的指路</param>
    /// <param name="stateOf">运行态判定；缺省用全局运行登记（测试传自建）</param>
    /// <returns>要追加的段落；没有委派时为空串</returns>
    public static string BuildSubSessionRoster(IReadOnlyList<ChatSessionMeta> subSessions, IEnumerable<AITool>? tools,
        Func<string, ESessionRunState>? stateOf = null)
    {
        if (subSessions.Count == 0) return string.Empty;

        stateOf ??= SessionManager.Instance.Running.StateOf;
        StringBuilder roster = new();
        roster.AppendLine();
        // 还在跑的那几位单独点名:压缩后模型要知道还有哪些活挂着(全部状态与编号可由扩展现查,这里只给抓手)
        List<ChatSessionMeta> running = subSessions
            .Where(x => stateOf(x.SessionId) != ESessionRunState.Idle)
            .ToList();
        if (running.Count == 0)
        {
            roster.Append($"本会话派出过 {subSessions.Count} 位子代理。");
        }
        else
        {
            roster.Append($"本会话派出过 {subSessions.Count} 位子代理，其中 {running.Count} 位还在工作中：\n");
            foreach (ChatSessionMeta meta in running)
            {
                string state = stateOf(meta.SessionId) == ESessionRunState.AwaitingApproval ? "等审批" : "工作中";
                // 身份行与子代理列表、回信信头共用同一份口径（SubAgentDescriptor），状态是当下才有的信息
                roster.Append(SubAgentDescriptor.Describe(meta)).Append(" · ").Append(state).Append('\n');
            }
        }

        if (tools?.Any(t => t.Name == ExtensionCalls.CallName) == true)
        {
            roster.Append("要查他们的状态或编号，用 "
                          + $"`{ExtensionCalls.CallName}` 调 `{BuiltinExtensions.ChatSession}` 扩展的 "
                          + $"`{BuiltinExtensions.ChatSessionList}` 命令；要续聊，把列出的 `[sub-session: …]` 编号作为 `to` 传给 "
                          + $"`{SubAgentTool.MessageToolName}`，而不是新开 "
                          + $"`{SubAgentTool.ToolName}`——新开会丢掉他们已整理好的一切。");
        }

        return roster.ToString().TrimEnd();
    }

    /// <summary>
    /// 回查段，<b>确定性追加</b>在记忆压缩末尾：最近最多 <see cref="RecallUserMessages"/> 条用户原话（逐条原文不截断），
    /// 加上纯文本转录的路径。
    ///
    /// 用户原话是意图与约束的第一手来源，"别动 X"这类约束在摘要里最容易丢，所以由代码原样带上。
    /// 只数<b>用户本人</b>说的话：系统注入（目标模式等）、群投递、子代理交回、知识库片段都不是用户的话，一律不算。
    /// 条数封顶 <see cref="RecallUserMessages"/>、不按篇幅截断——超长粘贴原样进回忆段是明确口径，
    /// 更早的原话与完整过程靠转录回查。每次都从整份历史现算，多次压缩不会逐次累积。
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <param name="transcriptPath">转录文件路径；没挂 Grep/Read 时为 null，路径与回查提示都不写</param>
    /// <returns>要追加的段落（以空行开头）；无可追加时为空串</returns>
    public static string BuildRecall(IReadOnlyList<ChatMessage> history, string? transcriptPath)
    {
        List<string> quotes = [];
        int older = 0;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            string? words = UserWords(history[i]);
            if (words == null) continue;

            if (quotes.Count >= RecallUserMessages)
            {
                older++;
                continue;
            }

            quotes.Add($"- #{i + 1}: {words}");
        }

        StringBuilder recall = new();
        if (quotes.Count > 0)
        {
            quotes.Reverse();
            recall.Append($"\n最近用户原话（最多 {RecallUserMessages} 条，原文照抄，旧在前；#N 是消息编号）：\n");
            foreach (string quote in quotes) recall.Append(quote).Append('\n');
        }

        if (transcriptPath != null)
        {
            string earlier = older > 0 ? $"以及更早的 {older} 条用户消息" : string.Empty;
            recall.Append($"\n这次记忆压缩之前的完整对话（包括被整理掉的部分{earlier}）以纯文本存在 \"{transcriptPath}\"："
                          + "每条消息一个 \"## #N 角色 时间\" 标题，工具调用与结果压成一行。"
                          + $"用 `{FileToolNames.Grep}` 搜确切措辞、路径或决定，再用 `{FileToolNames.Read}` 读命中处附近。\n");
        }

        return recall.ToString().TrimEnd();
    }

    // 用户本人说的话;点名技能取用户敲的那一行,正文是整份技能说明
    private static string? UserWords(ChatMessage message)
    {
        if (message.Role != ChatRole.User) return null;
        if (ChatMessageAnnotations.IsNotUserWords(message)) return null;
        AdditionalPropertiesDictionary? properties = message.AdditionalProperties;
        if (properties != null && NotUserWords.Any(properties.ContainsKey)) return null;
        if (properties?.TryGetValue(ChatMessageAnnotations.NamedSkillInput, out object? input) == true)
        {
            return input?.ToString();
        }

        string text = message.Text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// 在模型写的正文之后接上附加段，中间用 <see cref="AppendixHeading"/> 隔开
    /// </summary>
    /// <param name="note">模型写的记忆压缩正文</param>
    /// <param name="sections">各附加段（委派清单、回查段）；空串跳过</param>
    /// <returns>整份记忆压缩；没有附加段时即正文本身</returns>
    public static string WithAppendix(string note, params string[] sections)
    {
        string appendix = string.Join("\n\n", sections.Where(x => x.Length > 0).Select(x => x.Trim()));
        return appendix.Length == 0 ? note : $"{note}\n\n{AppendixHeading}\n{appendix}";
    }

    /// <summary>
    /// 构造记忆压缩消息。
    ///
    /// 角色取 <b>assistant</b> 而不是 system：文档是「我」以第一人称整理的记忆，
    /// 放在供给窗口的最前面作为<b>模型自己的第一条消息</b>，新一轮从这里接着往下说。
    /// 代价是失去框架截断的保护（截断从最老的非 system 组开始删，system 组一律保留）——
    /// 记忆压缩一旦被那一轮请求排除，模型那一轮就丢了前情；但轮末 0.8 水位会再写一份，自愈。
    /// </summary>
    /// <param name="note">文档正文</param>
    /// <returns>可直接写进历史的消息</returns>
    public static ChatMessage CreateNote(string note)
    {
        return new ChatMessage(ChatRole.Assistant, $"[{Title}]\n{note}")
        {
            CreatedAt = DateTimeOffset.Now,
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Handoff] = Title },
        };
    }

    private const double CapSlack = 1.5; //给模型一点余量,超过这个倍数才动刀

    /// <summary>
    /// 篇幅兜底。字数限制是写在提示词里的，模型经常无视——真放它写成一篇长文，
    /// 压缩就等于没做，甚至一写完又过水位，陷入「压缩→还是满→再压缩」的循环。
    /// 从尾部切（后面是"下一步"与关键细节，比开头的背景交代更该留），并明说被截断了。
    /// </summary>
    /// <param name="text">模型产出</param>
    /// <param name="charLimit">建议上限</param>
    /// <returns>不超过兜底长度的文本</returns>
    internal static string Cap(string text, int charLimit)
    {
        int hardLimit = (int)(charLimit * CapSlack);
        if (text.Length <= hardLimit) return text;

        Log.Warning($"Context handoff ran long ({text.Length} chars, limit {charLimit}); truncated.");
        return string.Concat(text.AsSpan(0, hardLimit), "\n…(truncated)");
    }

    /// <summary>
    /// 是否为记忆压缩消息
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是则 true</returns>
    public static bool IsNote(ChatMessage message)
    {
        return message.AdditionalProperties?.ContainsKey(ChatMessageAnnotations.Handoff) == true;
    }

    /// <summary>
    /// 取记忆压缩的正文（去掉给模型认的那行标题，界面上已有标题了）
    /// </summary>
    /// <param name="text">消息文本</param>
    /// <returns>正文</returns>
    public static string NoteBody(string text)
    {
        string prefix = $"[{Title}]";
        if (text.StartsWith(prefix, StringComparison.Ordinal)) return text[prefix.Length..].TrimStart();
        // 旧存档里标题还是 "Context handoff":改名之前写下的记忆压缩,界面上不能带着旧标题前缀
        string legacy = "[Context handoff]";
        return text.StartsWith(legacy, StringComparison.Ordinal) ? text[legacy.Length..].TrimStart() : text;
    }

    /// <summary>
    /// 拼出记忆压缩正文：固定开场 + 模型写的正文 + 上一轮最后一句（开场与末句由代码确定性追加）。
    /// 开场让卡片读起来有头；上一轮最后一句放末尾，让新一轮的接续点紧贴用户接下来的输入。
    /// </summary>
    /// <param name="note">模型写的正文</param>
    /// <param name="lastSentence">上一轮最后一句；没有（无 assistant 正文）时为 null</param>
    /// <returns>整段正文</returns>
    public static string ComposeBody(string note, string? lastSentence)
    {
        string body = $"{Opening}\n\n{note}";
        if (!string.IsNullOrWhiteSpace(lastSentence))
        {
            body += $"\n\n{LastSentenceHeading}\n{lastSentence}";
        }

        return body;
    }

    /// <summary>
    /// 取历史里最后一条 assistant 文本消息的最后一句话。
    /// 记忆压缩本身也是 assistant 角色，要跳过；开场旁白不是"上一轮说的话"，也跳过；
    /// 纯工具调用/空回复没有文本，跳过。没有可取的句子时返回 null（正文不带这段）。
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <returns>上一轮最后一句；取不到为 null</returns>
    public static string? LastAssistantSentence(IReadOnlyList<ChatMessage> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            ChatMessage message = history[i];
            if (message.Role != ChatRole.Assistant) continue;
            if (IsNote(message) || ChatMessageAnnotations.IsNarration(message)) continue;
            string text = message.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            return LastSentence(text);
        }

        return null;
    }

    /// <summary>无句末标点可切时的兜底长度：别把一大块代码/日志整个塞进记忆压缩</summary>
    private const int TailFallbackChars = 300;

    /// <summary>
    /// 切最后一句：按句末标点从后往前取最后一段。
    /// 英文句号只在该处是句尾（后跟空白或结尾、且前面不是数字）时才算，避免 "v1.2.3" 被劈开。
    /// </summary>
    private static string LastSentence(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return text;

        int last = LastSentenceEnd(text);
        if (last < 0)
        {
            return text.Length <= TailFallbackChars ? text : "…" + text[^TailFallbackChars..];
        }

        if (last == text.Length - 1)
        {
            // 以标点结尾:取这个标点所在的那一句(往回找上一个标点)
            int prev = LastSentenceEnd(text[..last]);
            return prev < 0 ? text : text[(prev + 1)..].Trim();
        }

        // 标点后面还有内容:取标点之后的末尾段
        return text[(last + 1)..].Trim();
    }

    private static int LastSentenceEnd(string text)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c is '。' or '！' or '？' or '…' or '!' or '?') return i;
            if (c == '.' && (i == text.Length - 1 || char.IsWhiteSpace(text[i + 1]))
                && (i == 0 || !char.IsDigit(text[i - 1])))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 取喂给模型的历史区间起点：最后一条记忆压缩所在的下标。
    /// 文档本身要包含在内——它就是模型能看到的全部前情。
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <returns>起点下标；没有记忆压缩时为 0</returns>
    public static int SupplyStartIndex(IReadOnlyList<ChatMessage> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (IsNote(history[i])) return i;
        }

        return 0;
    }

    /// <summary>
    /// 让模型写一份记忆压缩
    /// </summary>
    /// <param name="client">模型客户端</param>
    /// <param name="history">要压缩的历史，与常规请求同一视图（见 <see cref="HistorySupply.ForSideRequestAsync"/>）</param>
    /// <param name="agentOptions">本会话装配好的对话选项，见 <see cref="ICharacterRunner.ChatOptions"/></param>
    /// <param name="contextLength">当前模型的上下文上限，用于算出文档自己的篇幅上限</param>
    /// <param name="extraInstructions">
    /// 用户额外交代（<c>/compact</c> 命令后跟的文字）。原样附在写文档的指令末尾，
    /// 提示模型在文档里照顾到它；为空时不附加，指令与从前逐字一致。
    /// </param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>文档正文与这一发的用量；失败或产出为空时正文为 null</returns>
    public static async Task<HandoffReply> WriteAsync(IChatClient client, IReadOnlyList<ChatMessage> history,
        ChatOptions? agentOptions, int contextLength, string? extraInstructions = null,
        CancellationToken cancellationToken = default)
    {
        if (history.Count == 0) return new HandoffReply(null, []);

        int charLimit = NoteCharLimitFor(contextLength);
        string instruction = string.Format(Instruction, charLimit);
        if (!string.IsNullOrWhiteSpace(extraInstructions))
        {
            // 附在最后一条 user 指令消息里而不是另起一条:消息结构与从前完全一样,
            // 服务端的前缀缓存不因这段追加而失效
            instruction += "\n\n补充指示 — 与上面的一般清单冲突时优先执行：\n"
                           + extraInstructions.Trim();
        }

        List<ChatMessage> messages = new(history.Count + 1);
        messages.AddRange(history);
        messages.Add(new ChatMessage(ChatRole.User, instruction));

        // 选项**整份照搬**常规轮次的那一份(系统提示词 + 工具定义 + 采样参数):
        // 请求体的前缀是「system + 工具定义 + 消息」,少任何一段前缀就从那里岔开,
        // 服务端的前缀缓存整个作废——而这一发恰好是占用最高时发出的、最大的一次请求。
        // Clone 是必须的:直接改会污染 agent 自己在用的那一份
        ChatOptions options = agentOptions?.Clone() ?? new ChatOptions();

        List<UsageDetails> usages = [];
        try
        {
            // 第一发不禁工具:tool_choice 同样是请求体的一部分,写成 none 就与常规请求不再同形,
            // 服务端可能据它改写工具段的渲染。指令里已明说别调工具;
            // 它真去调了才禁掉重发一次(MEAI 的 ChatToolMode 没有 None,经逐请求上下文让 HTTP 层写 tool_choice)——
            // 那一发不中缓存,但少见,总好过每次都不中
            ChatResponse response = await client
                .GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
            if (response.Usage != null) usages.Add(response.Usage);
            if (CallsTools(response))
            {
                LlmRequestContext.ForbidToolCalls = true;
                response = await client
                    .GetResponseAsync(messages, options, cancellationToken)
                    .ConfigureAwait(false);
                if (response.Usage != null) usages.Add(response.Usage);
            }

            string text = response.Text.Trim();
            return new HandoffReply(text.Length == 0 ? null : Cap(text, charLimit), usages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warning($"Write context handoff failed: {e.Message}");
            return new HandoffReply(null, usages);
        }
        finally
        {
            LlmRequestContext.ForbidToolCalls = false;
        }
    }

    private static bool CallsTools(ChatResponse response)
    {
        return response.Messages.SelectMany(x => x.Contents).OfType<FunctionCallContent>().Any();
    }
}

/// <summary>
/// 写记忆压缩那一发请求的产出
/// </summary>
/// <param name="Text">文档正文；失败或产出为空时为 null</param>
/// <param name="Usages">
/// 每一发的用量（模型去调工具时会禁掉重发一次，于是有两发）；没发成或服务端不报时为空。
/// 正文为空也照样花了钱，所以与正文分开给；逐发给而不合并，是因为账本的「最近一次占用」要的是单发的数
/// </param>
public readonly record struct HandoffReply(string? Text, IReadOnlyList<UsageDetails> Usages);
