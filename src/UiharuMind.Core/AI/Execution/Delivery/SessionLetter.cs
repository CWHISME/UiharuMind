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

namespace UiharuMind.Core.AI.Execution.Delivery;

/// <summary>闲着时落进历史的结果</summary>
public enum ELetterWrite
{
    /// <summary>写进去了（追加或替换），该叫醒收信人</summary>
    Written,

    /// <summary>历史里已经是这一封了，什么都没改</summary>
    Unchanged,

    /// <summary>收信人或内容不在了，作罢</summary>
    Missing,
}

/// <summary>
/// 一封要送给某个会话的信（后台任务的结局、子代理的回信）。怎么送由 <see cref="SessionDelivery"/> 统一，
/// 信只管两件事：插进进行中那一轮时长什么样，收信人闲着时怎么落进历史
/// </summary>
public abstract class SessionLetter
{
    /// <summary>收信会话标识</summary>
    public abstract string SessionId { get; }

    /// <summary>起因，只进日志</summary>
    public abstract string Cause { get; }

    /// <summary>
    /// 组装插进那一轮的消息。每次插之前现组装：头部写的是此刻的事实（如还在等几位）
    /// </summary>
    /// <param name="session">收信会话</param>
    /// <returns>user 角色、带来源注记的消息；没有可插的为 null（那就等收信人闲下来再落）</returns>
    public abstract ChatMessage? Compose(ChatSession session);

    /// <summary>
    /// 收信人闲着时落进历史。调用方已持该会话的历史锁、确认它不在跑
    /// </summary>
    /// <param name="session">收信会话</param>
    /// <returns>落成了什么样</returns>
    public abstract ELetterWrite Write(ChatSession session);

    /// <summary>
    /// 同一来源的唯一键。<b>送达层按它去重</b>：全历史里已经有同键同正文的一封，就不再写也不注入——
    /// 子代理报告交回一次以上、后台任务正常送达与退出收尾各走一遍时，历史里只该有一封。
    /// null 表示不查重（这类信本身幂等，或就允许重复）。
    ///
    /// 键要能涵盖「回的是哪一次来信」这类区分：只按来源认的话，同一个人问两个问题回两封，
    /// 其中一封会被误当成另一封的重复而吞掉
    /// </summary>
    public virtual string? DedupeKey => null;

    /// <summary>
    /// 从历史消息里读它的来源键，与 <see cref="DedupeKey"/> 同一口径。
    /// 默认实现不认任何标记（配合 <see cref="DedupeKey"/> 为 null 的情形）；各信覆盖成读自己的标记
    /// </summary>
    /// <param name="message">历史里的一条消息</param>
    /// <returns>它的来源键；认不出为 null</returns>
    public virtual string? KeyOf(ChatMessage message) => null;
}
