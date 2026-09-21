/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 哪个界面壳能替某个会话接住<b>唤醒轮</b>的审批。
///
/// 唤醒轮由 <c>BackgroundSubAgentDispatcher</c> 在后台驱动（<c>sink</c> 为 null），
/// 于是那一轮的审批请求<b>只能</b>由挂在这个会话上的观察窗画出来并回应——
/// 驱动方自己那一头没有任何人。取不到宿主时按无头口径拒绝，与定时任务同形。
///
/// ⚠️ 这正是 ADR 0021 修订里嵌套审批踩过的那个坑的同构版：请求冒出来了，
/// 但接它的那一头清单恒为空、回应恒为空，于是轮次直接结束、调用永无结果。
/// 差别只在这里的宿主是<b>派活者自己的窗口</b>，不是子会话窗口。
/// </summary>
public static class WakeApprovalHosts
{
    private static readonly Dictionary<string, ApprovalResolver> _hosts = new();
    private static readonly object _locker = new();

    /// <summary>登记本壳为这个会话的唤醒轮审批宿主</summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="resolver">审批回应口</param>
    public static void Register(string? sessionId, ApprovalResolver resolver)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        // 后登记的覆盖先登记的:同一会话开了两个壳时,以最近挂上的那个为准。
        // 与「执行者是会话本体的单例」同一口径——多个壳看的是同一份东西,谁接都对
        lock (_locker) _hosts[sessionId] = resolver;
    }

    /// <summary>摘掉登记。只摘自己那一份：期间若已被别的壳顶替，不能把人家的摘掉</summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="resolver">登记时给出的那个回应口</param>
    public static void Unregister(string? sessionId, ApprovalResolver resolver)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_locker)
        {
            if (_hosts.TryGetValue(sessionId, out ApprovalResolver? current) && current == resolver)
            {
                _hosts.Remove(sessionId);
            }
        }
    }

    /// <summary>这个会话此刻有没有壳能接住审批（观察落点据此决定放不放行审批卡）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>有则 true</returns>
    public static bool HasHost(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_locker) return _hosts.ContainsKey(sessionId);
    }

    /// <summary>取这个会话的唤醒轮审批回应口；没有宿主返回 null（按拒绝收口）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>回应口；没有为 null</returns>
    public static ApprovalResolver? Resolve(string sessionId)
    {
        lock (_locker) return _hosts.GetValueOrDefault(sessionId);
    }
}
