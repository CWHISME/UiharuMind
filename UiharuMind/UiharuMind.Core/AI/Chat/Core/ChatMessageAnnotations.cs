/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

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
}
