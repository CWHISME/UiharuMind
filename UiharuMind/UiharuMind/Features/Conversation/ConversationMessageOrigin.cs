/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Features.Conversation;

/// <summary>一条历史消息在界面上该被当成什么来渲染</summary>
public enum EHistoryItemKind
{
    /// <summary>交接文档（压缩产物），画成独立卡片</summary>
    HandoffNote,

    /// <summary>旁白（开场白），居中无头像</summary>
    Narration,

    /// <summary>知识库检索片段，画成检索卡片</summary>
    Knowledge,

    /// <summary>子会话交回的后续报告，借旁白那套呈现</summary>
    SubAgentReport,

    /// <summary>用户输入（含插话）</summary>
    UserInput,

    /// <summary>助手与工具的内容，交给转录器按 <see cref="AIContent"/> 装配</summary>
    StreamContents,
}

/// <summary>
/// 消息的渲染归属。<b>这是「谁渲染哪一段」唯一的判据</b>——历史回放与实时流观察
/// 两条路都问它，避免各写一套 if 链之后悄悄漂移。
///
/// 为什么需要它：一轮跑起来之后，内容流与历史落盘都会到达界面。
/// 内容流产出的那些（助手正文、思考段、工具卡）由流渲染，落盘时只做条目与消息的配对；
/// 而流<b>产不出</b>的那几类（用户插话、检索卡、旁白、交接文档、后续报告）只能由历史渲染。
/// 分错一边的后果是静默的重复或缺失，所以判据只留一份。
/// </summary>
public static class ConversationMessageOrigin
{
    /// <summary>
    /// 这条消息该被当成什么来渲染
    /// </summary>
    /// <param name="message">历史消息</param>
    /// <returns>渲染种类</returns>
    public static EHistoryItemKind KindOf(ChatMessage message)
    {
        // 顺序即优先级:检索片段与后续报告的角色分别是 Tool 与 User,
        // 落到按角色分派的那一档就会被画成工具结果或用户气泡
        if (HistoryHandoff.IsNote(message)) return EHistoryItemKind.HandoffNote;
        if (ChatMessageAnnotations.IsNarration(message)) return EHistoryItemKind.Narration;
        if (ChatMessageAnnotations.IsKnowledge(message)) return EHistoryItemKind.Knowledge;
        if (ChatMessageAnnotations.IsSubAgentReport(message)) return EHistoryItemKind.SubAgentReport;
        if (message.Role == ChatRole.User) return EHistoryItemKind.UserInput;
        return EHistoryItemKind.StreamContents;
    }

    /// <summary>
    /// 这一类是不是由内容流产出的——是的话，一轮跑着的时候它已经由流渲染过，
    /// 落盘时不能再渲染一遍。
    ///
    /// 刻意写成 switch <b>表达式</b>且不留 discard：<see cref="EHistoryItemKind"/> 加一项
    /// 而没在这里表态，编译器当场报「未穷尽」（CS8509）。这条规矩不能靠记。
    /// </summary>
    /// <param name="kind">渲染种类</param>
    /// <returns>由内容流产出返回 true</returns>
    // CS8524 是「转换来的非法枚举值没覆盖」,补个 discard 就能消掉——但那会连 CS8509
    // (真正漏了一项具名值)一起吞掉,而后者正是这里要的护栏。只关掉前者
#pragma warning disable CS8524
    public static bool IsProducedByContentStream(EHistoryItemKind kind) => kind switch
    {
        EHistoryItemKind.StreamContents => true,
        EHistoryItemKind.HandoffNote => false,
        EHistoryItemKind.Narration => false,
        EHistoryItemKind.Knowledge => false,
        EHistoryItemKind.SubAgentReport => false,
        EHistoryItemKind.UserInput => false,
    };
#pragma warning restore CS8524
}
