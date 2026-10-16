/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 哪个界面壳能替某个会话接住<b>唤醒轮</b>的审批，以及那个会话是不是<b>一条都不问</b>。
///
/// 唤醒轮由 <c>BackgroundSubAgentDispatcher</c> 在后台驱动（<c>sink</c> 为 null），
/// 于是那一轮的审批请求<b>只能</b>由挂在这个会话上的观察窗画出来并回应——
/// 驱动方自己那一头没有任何人。取不到宿主时按无头口径拒绝，与定时任务同形。
///
/// <b>「这个会话问不问人」只此一处说了算</b>：目标模式勾了「自动同意审批」时登记进来的
/// 无人值守策略（<see cref="SetUnattendedApprovals"/>）返回非空，会话窗口就据此把卡片
/// 就地收掉、不等用户点；唤醒轮那头则在没有壳可问时直接走它。两边问的是同一份答案，
/// 不会各判一套。
///
/// ⚠️ 这正是 ADR 0021 修订里嵌套审批踩过的那个坑的同构版：请求冒出来了，
/// 但接它的那一头清单恒为空、回应恒为空，于是轮次直接结束、调用永无结果。
/// 差别只在这里的宿主是<b>派活者自己的窗口</b>，不是子会话窗口。
/// </summary>
public static class WakeApprovalHosts
{
    private static readonly Dictionary<string, ApprovalResolver> _hosts = new();
    private static readonly Dictionary<string, Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>>>
        _settled = new();
    private static readonly object _locker = new();
    private static Func<string, ApprovalResolver?>? _unattended; //壳问不到时的无人值守策略(目标模式的自动同意)

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

    /// <summary>
    /// 登记无人值守的审批策略：某个会话的审批一条都不问时，由它收口。
    /// 由装配入口调一次（<c>App</c> 启动时），与 <see cref="Register"/> 同一份口径
    /// </summary>
    /// <param name="unattended">按会话取无人值守的审批通道；没有这种会话返回 null</param>
    public static void SetUnattendedApprovals(Func<string, ApprovalResolver?>? unattended)
    {
        lock (_locker) _unattended = unattended;
    }

    /// <summary>这个会话此刻有没有壳能接住审批（观察落点据此决定放不放行审批卡）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>有则 true</returns>
    public static bool HasHost(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_locker) return _hosts.ContainsKey(sessionId);
    }

    /// <summary>
    /// 这个会话的审批是不是一条都不问：勾了「自动同意审批」时为那个通道，否则 null。
    ///
    /// 会话窗口据此把待决卡片就地收掉；唤醒轮 <see cref="Resolve"/> 在问不到壳时也走它
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>无人值守的审批通道；没有为 null</returns>
    public static ApprovalResolver? UnattendedFor(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        Func<string, ApprovalResolver?>? unattended;
        lock (_locker) unattended = _unattended;
        return unattended?.Invoke(sessionId);
    }

    /// <summary>
    /// 取这个会话的界面壳：只认壳，不含无人值守。唤醒轮的判据与收卡分开走时用它
    /// （判据走 <see cref="UnattendedFor"/> 取到的本轮通道，卡片另经 <see cref="SettledFor"/> 收掉）
    /// </summary>
    /// <param name="sessionId">会话标识；没有则问不出名堂</param>
    /// <returns>壳的回应口；没开着为 null</returns>
    public static ApprovalResolver? ResolveHost(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        lock (_locker) return _hosts.GetValueOrDefault(sessionId);
    }

    /// <summary>
    /// 登记无人值守决出后的收卡回调：唤醒轮用自己的通道判据，卡片仍要在这个会话的窗口里就地收掉。
    /// 与 <see cref="Register"/> 同一生命周期，摘壳时一并摘掉
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="settled">收卡回调（请求与一一对应的回应）</param>
    public static void RegisterSettled(string? sessionId,
        Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>> settled)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_locker) _settled[sessionId] = settled;
    }

    /// <summary>摘掉收卡回调；只摘自己那一份</summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="settled">登记时给出的那个回调</param>
    public static void UnregisterSettled(string? sessionId,
        Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>> settled)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        lock (_locker)
        {
            if (_settled.TryGetValue(sessionId, out Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>>? current)
                && current == settled)
            {
                _settled.Remove(sessionId);
            }
        }
    }

    /// <summary>取这个会话的收卡回调；没有为 null</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>收卡回调；没有为 null</returns>
    public static Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>>? SettledFor(
        string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        lock (_locker) return _settled.GetValueOrDefault(sessionId);
    }

    /// <summary>
    /// 取这个会话的唤醒轮审批回应口：先问壳（壳那一头自己知道要不要把卡片收掉），
    /// 壳没开着才走无人值守策略；两者都没有返回 null（按拒绝收口）。
    ///
    /// 单次查询语义：每次调用都新建无人值守通道（计数从零起）。调用方按执行持有一次、
    /// 后续批次复用，不得每批现取——否则轮次上限永不触发
    /// </summary>
    /// <param name="sessionId">会话标识；没有则问不出名堂</param>
    /// <returns>回应口；没有为 null</returns>
    public static ApprovalResolver? Resolve(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        // 先把两份答案拷贝出来、出锁再调：回调里可能再拿别的锁（目标模式控制器那把），锁里调会锁序反转
        ApprovalResolver? host;
        Func<string, ApprovalResolver?>? unattended;
        lock (_locker)
        {
            _hosts.TryGetValue(sessionId, out host);
            unattended = _unattended;
        }

        return host ?? unattended?.Invoke(sessionId);
    }
}
