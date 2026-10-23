/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Delivery;
using UiharuMind.Core.AI.Execution.Prompts;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 子代理给主代理（派活者）发消息的工具（SendMessage）。委派那条单向通道的<b>反向</b>：
/// 主代理经 <see cref="SubAgentTool.MessageToolName"/> 找子代理，这里让子代理找回去——
/// 干活中要信息、请拍板、报进度都可以开口。
///
/// 收件人<b>固定是派活者</b>，没有 <c>to</c> 参数可填错（同 <see cref="GroupPostTool"/> 的哲学）。
/// 子代理不往下委派（场合表不含 SubAgent 档），这把只发往唯一的父会话，递归无从发生。
///
/// <b>投递走 <see cref="SessionDelivery"/></b>：派活者醒着就插进那一轮，闲着落进历史再起唤醒轮。
/// 派活者回话仍用自己那把 SendMessage（<see cref="SubAgentTool.MessageToolName"/>）——
/// 回复经既有插话通道送回，以【发信人】前缀 + ParentInterjection 标记插回子代理正在写的那一轮。
/// </summary>
public static class ParentMessageTool
{
    /// <summary>工具名。与委派续聊、群发言同名（<see cref="SubAgentTool.MessageToolName"/>、<see cref="GroupPostTool.ToolName"/>）；
    /// 三者从不同场，由不变量测试钉住</summary>
    public const string ToolName = "SendMessage";

    /// <summary>
    /// 创建子代理侧的联系工具
    /// </summary>
    /// <param name="sessionId">子会话标识（发信人自己）</param>
    /// <returns>工具实例</returns>
    public static AITool Create(string sessionId) =>
        ToolJson.CreateFunction(
            ([Description(SubAgentToolPrompts.ParentMessageMessageParam)]
                string message) => Send(sessionId, message),
            ToolName,
            SubAgentToolPrompts.ParentMessageDescription);

    private static string Send(string sessionId, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Error: 消息不能为空。";

        ChatSession? sub = SessionManager.Instance.Load(sessionId);
        if (sub?.IsSubSession != true || string.IsNullOrEmpty(sub.ParentSessionId))
        {
            return "Error: 这不是一次委派的子会话，没有可以联系的主代理。";
        }

        string parentId = sub.ParentSessionId!;
        // 点名的人用名字，匿名的用派活时给的身份（与 SubAgentReportHandoff 同一份，署名也共用）
        string text = BuildText(SubAgentReportHandoff.SenderOf(sub), sub.SessionId, message.Trim());

        // 投递放后台、按子会话串行（复用报告交回那把闸）：不阻塞子代理这一轮，
        // 同一子代理的多封信按序送达；失败只落日志（见 <see cref="BackgroundSubAgentDispatcher"/>）
        _ = BackgroundSubAgentDispatcher.DeliverParentMessageAsync(sub.SessionId,
            new Letter(parentId, sub.SessionId, text));

        return SubAgentToolPrompts.ParentMessageReceipt;
    }

    /// <summary>
    /// 落进派活者历史的那封信的正文
    /// </summary>
    /// <param name="sender">署名（点名的人用名字，匿名的用派活时给的身份）</param>
    /// <param name="subSessionId">子会话标识（写短号，派活者照抄进 to）</param>
    /// <param name="message">子代理的话</param>
    /// <returns>信的全文</returns>
    internal static string BuildText(string sender, string subSessionId, string message)
    {
        string shortId = SubSessionIdAlias.Short(subSessionId);
        return $"来自 {SubAgentReportHandoff.FromLabel(sender, subSessionId)}，干活途中发来的消息：\n\n{message}\n\n"
               + $"【他在等你回话。用 `{SubAgentTool.MessageToolName}`，to 填 [sub-session: {shortId}] "
               + "回复他；你的回复会插进他正在写的那一轮。】";
    }

    /// <summary>子代理 → 主代理的信。写是<b>纯追加</b>：它是一次对话，不是交回，不占报告的槽位
    /// （<see cref="SubAgentReportHandoff"/> 的原地替换只对同一封回信成立）</summary>
    internal sealed class Letter : SessionLetter
    {
        private readonly string _childId;
        private readonly string _text;

        public Letter(string parentId, string childId, string text)
        {
            SessionId = parentId;
            _childId = childId;
            _text = text;
        }

        public override string SessionId { get; }

        public override string Cause => $"sub-agent message {_childId}";

        public override ChatMessage? Compose(ChatSession session)
        {
            ChatMessage message = new(ChatRole.User, _text);
            ChatMessageAnnotations.MarkSubAgentMessage(message, _childId);
            return message;
        }

        public override ELetterWrite Write(ChatSession session)
        {
            int from = session.History.Count;
            session.History.Add(Compose(session)!);
            session.SaveAppended(from);
            return ELetterWrite.Written;
        }
    }
}
