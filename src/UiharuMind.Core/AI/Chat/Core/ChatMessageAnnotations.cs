/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 挂在 <c>ChatMessage.AdditionalProperties</c> 上的标记键。
///
/// 这些键分属两个<b>正交</b>的轴，不要混用：
/// <list type="bullet">
/// <item><description><b>归属</b>——要不要写进我们的历史：<see cref="Attribution"/></description></item>
/// <item><description><b>呈现</b>——气泡怎么渲染：<see cref="NamedSkill"/> 与 <see cref="NamedSkillInput"/></description></item>
/// </list>
///
/// 一度只有 <see cref="Attribution"/> 一个键同时兼这两职，于是「要落盘、但需特殊渲染」
/// 这种消息（点名调用）根本无法表达——复用它会让技能正文不落盘、技能下一轮静默失效。
/// 新增标记前先想清楚落在哪个轴上。
///
/// 集中放在这里的另一个原因：UI 层不能引用 <c>SessionChatHistoryProvider</c>
/// （它的基类是框架类型，被 <c>PrivateAssets=compile</c> 挡住），
/// 键若定义在那里，UI 只能硬写字面量，改名就会静默失效。
/// </summary>
public static class ChatMessageAnnotations
{
    /// <summary>
    /// 溯源标记。框架各 provider 注入的消息（历史回放副本、todo 快照、mode 切换通知、记忆片段等）
    /// 带此键，含义是<b>不属于我们的历史</b>：既不落盘，也不渲染为用户气泡。
    /// 它们每轮由各 provider 重新生成，一旦写进历史就会逐轮累积并被回灌。
    /// </summary>
    public const string Attribution = "_attribution";

    /// <summary>
    /// 点名调用标记，值为被点名的技能名。带此键的消息<b>要落盘</b>——
    /// 技能正文必须常驻历史才能持续生效，只是气泡渲染成折叠形态。
    /// </summary>
    public const string NamedSkill = "_namedSkill";

    /// <summary>
    /// 点名调用标记，值为用户原样输入的那一行（<c>/技能名 参数</c>），气泡显示用。
    /// 落盘往返后值会变成 <c>JsonElement</c>，读取一律经 <c>ToString</c>，不能强转 string。
    /// </summary>
    public const string NamedSkillInput = "_namedSkillInput";

    /// <summary>
    /// 交接文档标记。带此键的消息<b>要落盘</b>——它是压缩后模型唯一能看到的前情，
    /// 丢了等于那段历史白压；渲染成一张独立的交接卡片而不是普通气泡。
    ///
    /// 它同时是<b>历史供给的起点</b>：喂给模型的历史从最后一条带此键的消息开始，
    /// 它之前的消息只留在会话文件与界面上。因此这个键落在「呈现」轴上，
    /// 不能复用 <see cref="Attribution"/>——那个键的含义是「不落盘」，正好相反。
    /// </summary>
    public const string Handoff = "_handoff";

    /// <summary>
    /// 知识库检索片段标记。带此键的消息<b>要落盘、要渲染，但不供给模型</b>——
    /// 全仓第一条「存而不供」的消息，上面两个轴都表达不了它。
    ///
    /// 落盘是为了重载会话后还能回溯「当时检索到了什么」（工具路径天然有这个能力，
    /// 注入路径不落盘就永远没有）；不供给是因为片段每轮按当前提问重新检索，
    /// 旧片段回灌给模型只会逐轮累积一堆过期上下文。
    /// 因此 <c>SessionChatHistoryProvider.ProvideChatHistoryAsync</c> 供给时必须滤掉它。
    /// </summary>
    public const string Knowledge = "_knowledge";

    /// <summary>
    /// 旁白标记。目前唯一带此键的是<b>开场白</b>：它是一条货真价实的 assistant 消息，
    /// 要落盘、要供给模型（模型得知道自己已经开过场，否则首轮又自我介绍一遍），
    /// 只是渲染成居中的旁白而不是角色气泡。因此它落在「呈现」轴上。
    ///
    /// 从前这件事靠 <c>AuthorName = "Narrator"</c> 那个哨兵字符串加
    /// 「历史为空且是 assistant」的隐式判定表达，而渲染层从来没读过它——
    /// 显示名字段兼职类型标记，两头都不牢。
    /// </summary>
    public const string Narration = "_narration";

    /// <summary>
    /// 后续报告标记，值为产出它的那个子会话标识。带此键的消息<b>要落盘、要供给模型</b>——
    /// 它是用户在子会话里点「交回主代理」送进来的结论，形状同 <see cref="Narration"/>：
    /// 一条货真价实的消息，只是渲染成一张独立卡片。
    ///
    /// 派活者醒着时先插进那一轮，落进历史才算送到；插不进、没被取走或取走了没落盘，才由我们直接落进历史
    /// （<c>SessionDelivery</c>，ADR 0062）。两条路落下的是同一条带此键的消息。
    /// </summary>
    public const string SubAgentReport = "_subAgentReport";

    /// <summary>
    /// 后台任务结果标记，值为任务编号。带此键的消息<b>要落盘、要供给模型</b>——
    /// 任务结束后送回启动它的会话，形状同 <see cref="SubAgentReport"/>：一条真消息，只是渲染成卡片。
    /// 送法也同那一条。
    /// </summary>
    public const string BackgroundTaskReport = "_backgroundTaskReport";

    /// <summary>
    /// 派活方插话标记。带此键的 user 消息是<b>派活方（主代理）</b>在子代理运行中经
    /// <c>SendMessage</c>（写子会话编号）实时插的话，不是用户在子会话窗口说的话——子代理提示词明确区分这两者。
    ///
    /// 它不落在上面任何一轴：消息<b>要落盘、要供给模型</b>（它是子会话历史的一部分），
    /// 只是来源需要被认出来——报告归因据此把「用户插话」与「派活方插话」分开交代。
    /// 因此它绝不能复用 <see cref="Attribution"/>（那个键的含义是「不落盘」）。
    /// </summary>
    public const string ParentInterjection = "_parentInterjection";

    /// <summary>
    /// 思考耗时标记：值为毫秒数。带此键的消息<b>要落盘、不供给模型判断</b>——
    /// 它是呈现轴：回放时思考卡片据此冻结显示真实耗时，而不是按重建时刻现算一个 0.1s。
    ///
    /// 与 <see cref="ThinkingChars"/> 成对出现：速度由两数现算，不另存。
    /// 同一条消息有多段思考时存合并值（耗时与字数各自求和），删除/分叉/压缩改写历史时不会错位。
    /// </summary>
    public const string ThinkingDurationMs = "_thinkingDurationMs";

    /// <summary>
    /// 思考字数标记：值为字符数，与 <see cref="ThinkingDurationMs"/> 成对出现。
    /// </summary>
    public const string ThinkingChars = "_thinkingChars";

    /// <summary>
    /// 群发言的发言人：值为角色标识。只出现在群流水里的 assistant 消息上——
    /// 群壳挂的是占位的空角色，不标的话整段群流水都画成同一个人的头像（ADR 0046）。
    /// </summary>
    public const string GroupSpeaker = "_groupSpeaker";

    /// <summary>
    /// 群发言出自哪个成员会话：值为会话标识，与 <see cref="GroupSpeaker"/> 成对出现。
    /// 投递时据此跳过发言人自己的话——那条本来就在他自己的会话里。
    /// </summary>
    public const string GroupSpeakerSession = "_groupSpeakerSession";

    /// <summary>
    /// 化身替用户说的群发言：值为化身会话标识（ADR 0055）。消息本身是用户发言，成员看到的就是用户说的；
    /// 这个标记只给界面画「化身」、给回执认它，投递时据此跳过化身自己的话
    /// </summary>
    public const string GroupAvatarPost = "_groupAvatarPost";

    /// <summary>
    /// 离席回执：群流水里一条只给人看的记录，值为回执 JSON（ADR 0055）。<b>永不投递</b>给成员与化身
    /// </summary>
    public const string GroupAwayReceipt = "_groupAwayReceipt";

    /// <summary>
    /// 群投递标记：成员会话里这条 user 消息是群里别人的发言（投递或插话），不是用户私聊说的话。
    /// 呈现轴：存储与供给仍是合成的一条，渲染按 <c>[名字]: </c> 拆成各发言人的气泡。
    /// 带它之前落盘的旧投递没有标记，渲染侧另有兜底
    /// </summary>
    public const string GroupDelivery = "_groupDelivery";

    /// <summary>
    /// 群成员私聊标记：成员会话里这条 user 消息是用户单独对他说的，发给模型的正文前带一句私聊说明
    /// （<c>GroupTranscript.PrivateNote</c>），界面显示时摘掉
    /// </summary>
    public const string GroupPrivate = "_groupPrivate";

    /// <summary>
    /// 进群标记：成员会话里这条回复已作为他的发言贴进群里。界面据此挂「已发到群」，
    /// 与调工具时顺手写的过程话（只留在他那里）分得开
    /// </summary>
    public const string GroupPosted = "_groupPosted";

    /// <summary>
    /// 摘掉框架盖上的 <see cref="Attribution"/> 溯源标记。
    ///
    /// 框架把供给出去的历史消息<b>就地</b>盖章（我们交出去的是同一批实例），
    /// 于是一条货真价实的用户输入在跑过一轮之后就带上了「不属于我们的历史」这个标记。
    /// 它<b>再次被当作本轮输入</b>（重新生成走的正是这条路：把原消息从历史里摘出来重跑）时，
    /// 持久化那一关会按标记把它滤掉——消息再也回不到历史，界面上还在，重开会话就没了。
    /// 因此凡是我们自己发起的一轮，输入消息一律先摘章：它按定义就是我们的。
    /// </summary>
    /// <param name="message">消息</param>
    public static void ClearAttribution(ChatMessage message)
    {
        message.AdditionalProperties?.Remove(Attribution);
    }

    /// <summary>
    /// 判断是否为旁白消息（开场白）
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>带 <see cref="Narration"/> 标记时返回 True</returns>
    public static bool IsNarration(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(Narration) == true;

    /// <summary>
    /// 判断是否为子会话的后续报告消息
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>带 <see cref="SubAgentReport"/> 标记时返回 True</returns>
    public static bool IsSubAgentReport(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(SubAgentReport) == true;

    /// <summary>
    /// 判断是否为后台任务结果消息
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>带 <see cref="BackgroundTaskReport"/> 标记时返回 True</returns>
    public static bool IsBackgroundTaskReport(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(BackgroundTaskReport) == true;

    /// <summary>
    /// 读后台任务结果对应的任务编号。落盘往返后值会变成 <c>JsonElement</c>，一律经 <c>ToString</c>
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>任务编号；不是后台任务结果时为空串</returns>
    public static string ReadBackgroundTaskReportId(ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(BackgroundTaskReport, out object? raw) != true) return string.Empty;
        return raw?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// 是不是<b>交回的报告</b>（子代理后续报告、后台任务结果）：user 角色、落盘也供给模型，但不是用户的话。
    /// 问「这条 user 消息是不是用户说的」「它是不是一张卡」时一律经它，不各自枚举标记——各写一份迟早漏一处
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是交回的报告时返回 True</returns>
    public static bool IsHandedBackReport(ChatMessage message) =>
        IsSubAgentReport(message) || IsBackgroundTaskReport(message);

    /// <summary>
    /// 读后续报告指向的子会话标识。落盘往返后值会变成 <c>JsonElement</c>，一律经 <c>ToString</c>
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>子会话标识；不是后续报告时为空串</returns>
    public static string ReadSubAgentReportSession(ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(SubAgentReport, out object? raw) != true) return string.Empty;
        return raw?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// 判断是否为派活方插话
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>带 <see cref="ParentInterjection"/> 标记时返回 True</returns>
    public static bool IsParentInterjection(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(ParentInterjection) == true;

    /// <summary>
    /// 给一条派活方插话盖上来源标记。就地写：调用方持有的是之后进注入队列的同一引用。
    /// </summary>
    /// <param name="message">派活方插话</param>
    public static void MarkParentInterjection(ChatMessage message)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[ParentInterjection] = true;
    }

    /// <summary>
    /// 给一条群发言盖上发言人。就地写：调用方随后把同一引用追加进群流水。
    /// </summary>
    /// <param name="message">群发言</param>
    /// <param name="characterId">发言人的角色标识</param>
    /// <param name="memberSessionId">发言人的成员会话标识</param>
    public static void MarkGroupPost(ChatMessage message, string characterId, string memberSessionId)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[GroupSpeaker] = characterId;
        message.AdditionalProperties[GroupSpeakerSession] = memberSessionId;
    }

    /// <summary>给一条用户发言盖上「化身替用户说的」。就地写：调用方随后把同一引用追加进群流水</summary>
    /// <param name="message">群发言（用户角色）</param>
    /// <param name="avatarSessionId">化身会话标识</param>
    public static void MarkGroupAvatarPost(ChatMessage message, string avatarSessionId)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[GroupAvatarPost] = avatarSessionId;
    }

    /// <summary>给一条群流水记录盖上离席回执。就地写</summary>
    /// <param name="message">记录</param>
    /// <param name="receiptJson">回执 JSON</param>
    public static void MarkGroupAwayReceipt(ChatMessage message, string receiptJson)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[GroupAwayReceipt] = receiptJson;
    }

    /// <summary>给一条投递进成员会话的群发言盖上标记。就地写：调用方随后交出去的是同一引用</summary>
    /// <param name="message">投递或插话</param>
    public static void MarkGroupDelivery(ChatMessage message)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[GroupDelivery] = true;
    }

    /// <summary>是不是投递进成员会话的群发言</summary>
    /// <param name="message">消息</param>
    /// <returns>是为 true</returns>
    public static bool IsGroupDelivery(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(GroupDelivery) == true;

    /// <summary>给一条群成员私聊盖上标记。就地写</summary>
    /// <param name="message">私聊消息</param>
    public static void MarkGroupPrivate(ChatMessage message)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[GroupPrivate] = true;
    }

    /// <summary>是不是群成员私聊</summary>
    /// <param name="message">消息</param>
    /// <returns>是为 true</returns>
    public static bool IsGroupPrivate(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(GroupPrivate) == true;

    /// <summary>给一条已贴进群的成员回复盖上标记。就地写</summary>
    /// <param name="message">成员会话里的回复</param>
    public static void MarkPostedToGroup(ChatMessage message)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[GroupPosted] = true;
    }

    /// <summary>这条成员回复是不是已贴进群</summary>
    /// <param name="message">消息</param>
    /// <returns>是为 true</returns>
    public static bool IsPostedToGroup(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(GroupPosted) == true;

    /// <summary>
    /// 这条消息身上的 <see cref="Attribution"/> 是不是框架<b>回灌历史</b>时盖的（来源 = 历史提供器）。
    ///
    /// 框架把历史交给模型时，会往<b>已有附加属性字典</b>的消息上就地盖来源——写进的是我们历史里的同一个对象，
    /// 整份重存时还会落盘。于是凡是带了自家标记的消息（点名调用、群投递、私聊…）都会被盖上，
    /// 而它们本来就是我们的历史，不是框架注入的。真正的注入（todo 快照、模式通知、记忆片段）来源是上下文提供器
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是历史回灌盖的为 true</returns>
    public static bool IsHistoryEcho(ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(Attribution, out object? raw) != true) return false;

        return raw switch
        {
            AgentRequestMessageSourceAttribution attribution =>
                attribution.SourceType == AgentRequestMessageSourceType.ChatHistory,
            // 落盘往返之后是 {"sourceType":{"value":"ChatHistory"},"sourceId":...}
            JsonElement { ValueKind: JsonValueKind.Object } element =>
                SourceTypeOf(element) == AgentRequestMessageSourceType.ChatHistory.Value,
            _ => false,
        };
    }

    private static string? SourceTypeOf(JsonElement attribution)
    {
        foreach (JsonProperty property in attribution.EnumerateObject())
        {
            if (!string.Equals(property.Name, "sourceType", StringComparison.OrdinalIgnoreCase)) continue;

            JsonElement type = property.Value;
            if (type.ValueKind == JsonValueKind.String) return type.GetString();
            if (type.ValueKind != JsonValueKind.Object) return null;
            foreach (JsonProperty inner in type.EnumerateObject())
            {
                if (string.Equals(inner.Name, "value", StringComparison.OrdinalIgnoreCase)) return inner.Value.GetString();
            }
        }

        return null;
    }

    /// <summary>读点名调用时用户原样输入的那一行</summary>
    /// <param name="message">消息</param>
    /// <returns>用户输入；不是点名调用消息为 null</returns>
    public static string? NamedSkillInputOf(ChatMessage message) => ReadString(message, NamedSkillInput);

    /// <summary>读群发言的发言人角色标识。落盘往返后值是 <c>JsonElement</c>，一律经 <c>ToString</c></summary>
    /// <param name="message">消息</param>
    /// <returns>角色标识；不是群发言为 null</returns>
    public static string? GroupSpeakerOf(ChatMessage message) => ReadString(message, GroupSpeaker);

    /// <summary>读群发言出自哪个成员会话</summary>
    /// <param name="message">消息</param>
    /// <returns>成员会话标识；不是成员的群发言（含用户发言）为 null</returns>
    public static string? GroupSpeakerSessionOf(ChatMessage message) => ReadString(message, GroupSpeakerSession);

    /// <summary>读化身替用户说的群发言出自哪个化身会话</summary>
    /// <param name="message">消息</param>
    /// <returns>化身会话标识；不是化身说的为 null</returns>
    public static string? GroupAvatarPostOf(ChatMessage message) => ReadString(message, GroupAvatarPost);

    /// <summary>读离席回执</summary>
    /// <param name="message">消息</param>
    /// <returns>回执 JSON；不是回执为 null</returns>
    public static string? GroupAwayReceiptOf(ChatMessage message) => ReadString(message, GroupAwayReceipt);

    private static string? ReadString(ChatMessage message, string key)
    {
        if (message.AdditionalProperties?.TryGetValue(key, out object? raw) != true) return null;
        string? value = raw?.ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// 判断是否为知识库检索片段消息。
    /// 判定放在这里而不是 <c>SessionChatHistoryProvider</c>：那个类 UI 层引用不到
    /// （基类是被 <c>PrivateAssets=compile</c> 挡住的框架类型），而回放渲染必须认得它。
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>带 <see cref="Knowledge"/> 标记时返回 True</returns>
    public static bool IsKnowledge(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(Knowledge) == true;

    /// <summary>
    /// 写一条消息的思考统计（耗时毫秒 + 字数）。就地写：调用方持有的是历史里的同一引用。
    /// </summary>
    /// <param name="message">目标消息</param>
    /// <param name="durationMs">思考耗时毫秒</param>
    /// <param name="chars">思考字数</param>
    public static void WriteThinkingStats(ChatMessage message, long durationMs, long chars)
    {
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties[ThinkingDurationMs] = durationMs;
        message.AdditionalProperties[ThinkingChars] = chars;
    }

    /// <summary>
    /// 读一条消息的思考统计。落盘往返后值会变成 <c>JsonElement</c>（见点名输入的同类问题），
    /// 因此按 long / int / double / string / JsonElement 逐一兼容，不强转。
    /// </summary>
    /// <param name="message">消息</param>
    /// <param name="duration">思考耗时</param>
    /// <param name="chars">思考字数</param>
    /// <returns>两个键齐全且合法返回 True</returns>
    public static bool TryReadThinkingStats(ChatMessage message, out TimeSpan duration, out long chars)
    {
        duration = TimeSpan.Zero;
        chars = 0;
        if (message.AdditionalProperties == null) return false;
        if (!TryGetInt64(message.AdditionalProperties, ThinkingDurationMs, out long ms)) return false;
        if (!TryGetInt64(message.AdditionalProperties, ThinkingChars, out chars)) return false;
        if (ms < 0 || chars < 0) return false;
        duration = TimeSpan.FromMilliseconds(ms);
        return true;
    }

    private static bool TryGetInt64(AdditionalPropertiesDictionary props, string key, out long value)
    {
        value = 0;
        if (!props.TryGetValue(key, out object? raw) || raw == null) return false;
        switch (raw)
        {
            case long l:
                value = l;
                return true;
            case int i:
                value = i;
                return true;
            case double d:
                value = (long)d;
                return true;
            case string s:
                return long.TryParse(s, out value);
            case JsonElement { ValueKind: JsonValueKind.Number } n when n.TryGetInt64(out long parsed):
                value = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } s:
                return long.TryParse(s.GetString(), out value);
            default:
                return false;
        }
    }
}
