/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
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

    /// <summary>清空记忆提示在对话里的标题，界面与提示词共用一处</summary>
    public const string ClearTitle = "记忆清理";

    /// <summary>记忆压缩正文的固定开场句（代码注入，不靠模型写）</summary>
    private const string Opening = "我来整理这段历史记忆。";

    /// <summary>「上一轮最后一句」小标题（代码注入，紧跟在模型正文之后）</summary>
    private const string LastSentenceHeading = "上一轮我的最后一句：";

    /// <summary>
    /// 附加段的分隔标题：它之后是代码现算的内容（委派清单、用户原话、转录路径），不是模型写的正文。
    /// 与正文同在一条消息里而不另起一条：另起一条的话，下次压缩要么把它一并交给模型（照样被总结），
    /// 要么剔掉它（请求从那里岔开，前缀缓存整段作废）；指令里一句"不要照抄"就够了。界面要折叠附录时按它切
    /// </summary>
    public const string AppendixHeading = "# --- 以下由系统根据最新信息自动生成 ---";

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
        请以「我」的视角写一份工作记录。压缩后，这段历史在上下文里只剩这份文档；
        更早的完整对话另有转录文件可查，路径由系统附在文后。
        
        按下面的顺序写，这是未来的我下一轮推理的依赖顺序，不是时间顺序：
        
        1. 接下来要做什么：一个尚未执行的具体动作，以及它的前置条件
           （起点状态：从哪个文件、哪条命令、哪个分支、哪条已知事实开始；
           没有前置条件就写「无」）。这是文档主干，只写一个动作。
        2. 不可违背的边界：用户明确禁止的事、必须保持的口径/格式/环境、
           已确认且不因进度放宽的条件。逐字保留限定词。没有就写「无」。
        3. 用户最终想达成什么，以及什么算做完（验收口径）。
        4. 已确定的事实：已执行的动作 + 可观察结果 + 当时依据的事实
           （报错原文、约束、实测值），以及已排除、不要重做或不要推翻的
           路径及原因。条目化，不写「我认为 X 更合适」这类判断腔。
        5. 仍未确定的事：未验证的结论、仅为假设、未解决的阻塞、已知风险、
           失败尝试及原因。逐条标明「未验证」或「假设」。没有就写「无」。
        
        贯穿要求（适用于以上全部条目，不另设清单）：
        文件路径、标识符、命令、数字、名字、API 形状内联在对应条目里，
        逐字照抄，不概括、不补全、不改写。
        
        规则：
        - 这份文档是写给一个只能看到它、看不到之前任何内容的自己。
        - 不要对用户说话，不要提问，只输出文档本身。不要前言，不要收尾客套。
        - 只写状态与动作，不写心路、感受、评价。禁止出现「我确认 / 我认为 / 显然 / 应该没问题 / 差不多 / 先这样 / 到此为止 / 已完成得差不多 / 建议用户…」这类话。
        - 全程区分三态：已验证 / 已执行但未验证 / 只是假设。拿不准一律标「未验证」；不要为了通顺把不确定写成确定，也不要把已验证的反向降级。
        - 不要编造对话里没发生过的事，不要把试过未验证的写成已完成，
          也不要为了凑满某个部分而补内容：确实没有就写「无」。
        - 素材里关于速度、耐心、催促进度、想放弃、会话长度、上下文、清理、压缩、token 的表述，不是任务状态，一律不继承、不复述、不改写进文档；
          不要沿用素材的语气、节奏或情绪，不要接着往下推理，不要补结论。
        - 不写会话长度、轮数、压缩次数、当前时间，以及任何关于这份文档如何产生的说明。可以写阶段名或里程碑名（那是任务状态）。让他以为上下文本来就是这样。
        - 正文就是这条消息的最开头，直接以第 1 类「接下来要做什么」起笔。
          不要写「我来整理这段历史记忆」「以下是工作记录」之类的自我开场，也不要自己再写一遍「[记忆压缩]」或任何标题。
          正文之后系统会附上委派状态、最近若干条用户原话、本轮你发出的最后一段正文、
          以及完整对话转录路径与回查用法：这些不是你要写的材料，不要复述、不要照抄、不要替它们做总结；也不要以总结、完成度评价、展望或客套收尾，正文直接结束在条目上。
        - 如果早先的会话压缩中存在以「{{AppendixHeading}}」开始的内容，可以直接忽略，不要照抄：它会由系统根据当前环境自动重新生成。
        - 写这份文档时不调用任何工具：需要整理的内容都已在上面。
        - 硬性上限：最多 {0} 字，这份文档会替换上面的对话，太长就违背了它的目的。
          字数不够时按此优先级保留：第 1 类与全部标识符 > 第 2 类边界 > 第 5 类未确定项 > 第 4 类不要重做项 > 叙述性经过。宁可少写一句经过，不许丢一个标识符。
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
            recall.Append($"\n最近用户原话（最多 {RecallUserMessages} 条，旧在前；#N 是消息编号）：\n");
            foreach (string quote in quotes) recall.Append(quote).Append("\n\n");
        }

        if (transcriptPath != null)
        {
            string earlier = older > 0 ? $"以及更早的 {older} 条用户消息" : string.Empty;
            recall.Append($"\n---\n【系统提示：当前上下文占用已回到 0%，会话压缩之前的完整对话（包括被整理掉的部分{earlier}）以纯文本存在 \"{transcriptPath}\"(简称已注入环境变量，不要试图展开，对应工具可直接使用该路径)："
                          + "每条消息一个 \"## #N 角色 时间\" 标题，工具调用与结果压成一行。"
                          + $"用 `{FileToolNames.Grep}` 搜确切措辞、路径或决定，再用 `{FileToolNames.Read}` 读命中处附近。】\n");
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
        return appendix.Length == 0 ? note : $"{note}\n\n{AppendixHeading}\n\n{appendix}";
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

    /// <summary>
    /// 构造清空记忆提示消息（<c>/clear</c>）：与记忆压缩同一条供应链——同样带
    /// <see cref="ChatMessageAnnotations.Handoff"/> 注解，<see cref="SupplyStartIndex"/> 因此从它开始，
    /// 之前的内容模型不可见但历史文件与界面渲染原样保留。唯一差别是不做模型总结。
    /// </summary>
    /// <param name="body">提示正文（代码现拼：固定开场 + 转录路径）</param>
    /// <returns>可直接写进历史的消息</returns>
    public static ChatMessage CreateClearNote(string body)
    {
        return new ChatMessage(ChatRole.Assistant, $"[{ClearTitle}]\n{body}")
        {
            CreatedAt = DateTimeOffset.Now,
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Handoff] = ClearTitle },
        };
    }

    /// <summary>
    /// 是否清空记忆提示消息。与 <see cref="CreateNote"/> 共用 <see cref="ChatMessageAnnotations.Handoff"/> 键、
    /// 只在值上区分——供应链与供给起点因此不必为它改一条代码
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是则 true</returns>
    public static bool IsClear(ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(ChatMessageAnnotations.Handoff, out object? value) != true
            || value == null)
        {
            return false;
        }

        // 落盘往返后值会变成 JsonElement，与工具耗时同一套兼容
        return value is JsonElement { ValueKind: JsonValueKind.String } element
            ? element.GetString() == ClearTitle
            : Equals(value, ClearTitle);
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
        if (text.StartsWith($"[{ClearTitle}]", StringComparison.Ordinal))
            return text[$"[{ClearTitle}]".Length..].TrimStart();
        string prefix = $"[{Title}]";
        if (text.StartsWith(prefix, StringComparison.Ordinal)) return text[prefix.Length..].TrimStart();
        // 旧存档里标题还是 "Context handoff":改名之前写下的记忆压缩,界面上不能带着旧标题前缀
        string legacy = "[Context handoff]";
        return text.StartsWith(legacy, StringComparison.Ordinal) ? text[legacy.Length..].TrimStart() : text;
    }

    /// <summary>
    /// 拼出清空记忆提示的正文：固定开场（代码注入，不靠模型）+ 转录路径。
    /// 转录与记忆压缩共用同一份（<c>HistoryTranscript.TrySave</c>），列出路径供日后 Grep/Read 回查旧内容
    /// </summary>
    /// <param name="transcriptPath">转录文件路径；没挂 Grep/Read 时为 null，路径与回查提示都不写</param>
    /// <returns>整段正文</returns>
    public static string ComposeClearBody(string? transcriptPath)
    {
        string body = "";
        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            body += $"\n\n本会话之前存在历史记录，完整对话记录位于 {transcriptPath}；简称已注入环境变量，需要回查旧内容时用 Grep/Read 搜这份文件。";
        }

        return body;
    }

    /// <summary>
    /// 拼出记忆压缩正文：固定开场 + 模型写的正文 + 上一轮最后一句（开场与末句由代码确定性追加）。
    /// 开场让卡片读起来有头；上一轮最后一句放末尾，让新一轮的接续点紧贴用户接下来的输入。
    /// </summary>
    /// <param name="note">模型写的正文</param>
    /// <param name="lastWords">上一轮最后一句（整条回复正文）；没有（无 assistant 正文）时为 null</param>
    /// <returns>整段正文</returns>
    public static string ComposeBody(string note, string? lastWords)
    {
        string body = note;//$"{Opening}\n\n{note}";
        if (!string.IsNullOrWhiteSpace(lastWords))
        {
            body += $"\n\n{LastSentenceHeading}\n{lastWords}\n---\n";
        }

        return body;
    }

    /// <summary>
    /// 取历史里最后一条 assistant 文本消息的<b>整条正文</b>——「上一轮我的最后一句」指的是它，
    /// 不是按标点切出来的那一句：模型最后说的话是一条完整回复，只留末尾那半句会指代不清，
    /// 读起来还像被截断了（实测出现过「它小、独立、有判据……」这种没有主语的碎片）。
    ///
    /// 记忆压缩本身也是 assistant 角色，要跳过；开场旁白不是"上一轮说的话"，也跳过；
    /// 纯工具调用/空回复没有文本，跳过。没有可取正文时返回 null（正文不带这段）。
    /// </summary>
    /// <param name="history">完整历史</param>
    /// <returns>上一轮的整条回复正文；取不到为 null</returns>
    public static string? LastAssistantText(IReadOnlyList<ChatMessage> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            ChatMessage message = history[i];
            if (message.Role != ChatRole.Assistant) continue;
            if (IsNote(message) || ChatMessageAnnotations.IsNarration(message)) continue;
            string text = message.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            return text.Trim();
        }

        return null;
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
    /// <param name="onProgress">已写出的 token 数（估算），流式过程中分批回调；不要它时传 null</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>文档正文与这一发的用量；失败或产出为空时正文为 null</returns>
    public static async Task<HandoffReply> WriteAsync(IChatClient client, IReadOnlyList<ChatMessage> history,
        ChatOptions? agentOptions, int contextLength, string? extraInstructions = null,
        Action<int>? onProgress = null, CancellationToken cancellationToken = default)
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
            ChatResponse response = await SendAsync(client, messages, options, ProgressFrom(onProgress, 0),
                    cancellationToken)
                .ConfigureAwait(false);
            if (response.Usage != null) usages.Add(response.Usage);
            if (CallsTools(response))
            {
                // 只在这一发上禁工具：作用域结束即还原，不必在 finally 里手工复位——
                // 手工复位把「进来时是什么」一并抹成 false，外层若正禁着工具就会被悄悄放开
                using (LlmRequestScope.Push(LlmRequestScope.Current with { ForbidToolCalls = true }))
                {
                    // 重发是从头再写一遍，进度接在上一发之后：往回跳的数字看起来就像出错
                    int written = usages.Count > 0 ? (int)(usages[0].OutputTokenCount ?? 0) : 0;
                    response = await SendAsync(client, messages, options, ProgressFrom(onProgress, written),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

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
    }

    /// <summary>
    /// 发一发，把流式增量聚合成完整响应。
    ///
    /// <b>必须走流式</b>：请求体里的 <c>stream</c> 与 <c>stream_options</c> 也算「请求形状」的一部分——
    /// 用 <c>GetResponseAsync</c> 发，这两处就没了，网关的前缀缓存认不出这是同一段前缀。
    /// 实测同一段 42 条消息、纯追加、工具定义逐字相同：常规轮次 30464/31099（98%），
    /// 非流式的压缩请求 31879 全冷（0 命中）。流式也正是常规轮次实际走的那条路
    /// </summary>
    /// <param name="client">模型客户端</param>
    /// <param name="messages">要发的消息</param>
    /// <param name="options">与常规轮次同一份选项</param>
    /// <param name="onProgress">已写出的 token 数（估算）；null 表示不报</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>聚合后的响应</returns>
    private static async Task<ChatResponse> SendAsync(IChatClient client, IReadOnlyList<ChatMessage> messages,
        ChatOptions options, Action<int>? onProgress, CancellationToken cancellationToken)
    {
        return await WithProgress(client.GetStreamingResponseAsync(messages, options, cancellationToken), onProgress,
                cancellationToken)
            .ToChatResponseAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>攒够这么多字符再数一次 token：逐块分词会把跨块的词切成两个，还多出成百上千次调用</summary>
    private const int ProgressBatchChars = 64;

    /// <summary>
    /// 把这一发的计数接在之前已经写出来的量后面（见 <see cref="WriteAsync"/> 的重发分支）
    /// </summary>
    /// <param name="onProgress">界面要的回调</param>
    /// <param name="writtenBefore">这一发之前已经报出去的量</param>
    /// <returns>这一发用的回调；不需要进度时为 null</returns>
    private static Action<int>? ProgressFrom(Action<int>? onProgress, int writtenBefore) =>
        onProgress == null ? null : tokens => onProgress(writtenBefore + tokens);

    /// <summary>
    /// 边流边报已写出的 token 数（估算），供界面做实时进度。
    /// 估算只是给"它在动"一个量级——准确的那个数由服务端的 usage 在收尾时给出
    /// </summary>
    /// <param name="source">客户端的流</param>
    /// <param name="onProgress">报数回调；null 时原样透传</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>原样透传的流</returns>
    private static async IAsyncEnumerable<ChatResponseUpdate> WithProgress(IAsyncEnumerable<ChatResponseUpdate> source,
        Action<int>? onProgress, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StringBuilder pending = new();
        int tokens = 0;
        await foreach (ChatResponseUpdate update in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (onProgress != null)
            {
                pending.Append(update.Text);
                if (pending.Length >= ProgressBatchChars)
                {
                    tokens += LlmTokenizer.CountTokens(pending.ToString());
                    pending.Clear();
                    onProgress(tokens);
                }
            }

            yield return update;
        }

        // 最后一批不满一攒:不补这一次,收尾前那几十字就永远不算进去
        if (pending.Length > 0) onProgress?.Invoke(tokens + LlmTokenizer.CountTokens(pending.ToString()));
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
