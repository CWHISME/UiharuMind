/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
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
    /// 之所以不走注入队列（<c>ICharacterRunner.TryInjectAsync</c>）：那条通道的消费时机
    /// 由模型下一次请求决定（要等多久不由我们控制），拿不到注入器时还会静默失败——
    /// 用它送一份来之不易的结论风险不对等。注入进队列的消息最终会随内容流落盘
    /// （见 <see cref="ParentInterjection"/>），但「什么时候被消费」不是我们能承诺的。
    /// 见 ADR 0021、0025。
    /// </summary>
    public const string SubAgentReport = "_subAgentReport";

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

    /// <summary>读群发言的发言人角色标识。落盘往返后值是 <c>JsonElement</c>，一律经 <c>ToString</c></summary>
    /// <param name="message">消息</param>
    /// <returns>角色标识；不是群发言为 null</returns>
    public static string? GroupSpeakerOf(ChatMessage message) => ReadString(message, GroupSpeaker);

    /// <summary>读群发言出自哪个成员会话</summary>
    /// <param name="message">消息</param>
    /// <returns>成员会话标识；不是成员的群发言（含用户发言）为 null</returns>
    public static string? GroupSpeakerSessionOf(ChatMessage message) => ReadString(message, GroupSpeakerSession);

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
