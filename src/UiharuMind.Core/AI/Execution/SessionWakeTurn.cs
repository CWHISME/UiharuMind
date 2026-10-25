/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 起一轮<b>唤醒轮</b>：没有用户消息，模型读的是刚落进历史的那条结果（ADR 0025）。
///
/// 自激封顶也在这里：唤醒轮里又派活、又被叫醒，用户一句话没说它自己转起来了。
/// 来源（后台子代理、后台任务）不分，一起计数（ADR 0062）
/// </summary>
public static class SessionWakeTurn
{
    /// <summary>
    /// 连续<b>无用户参与</b>的唤醒轮上限。掐的是自激空转，不是总次数——用户说一句话即清零。
    /// 取 32 而不是 12：主、子代理多轮讨论时每一轮都是无用户参与的唤醒轮，12 轮不够一次讨论收敛
    /// </summary>
    internal const int MaxConsecutiveWakeTurns = 32;

    private static readonly ConcurrentDictionary<string, int> _streaks = new(); //会话 → 连续无用户参与的唤醒轮数

    /// <summary>
    /// 唤醒轮的按会话策略。<b>由装配入口一次注满</b>（<c>App</c> 的 <c>WireBackgroundSubAgents</c>）：
    /// Core 不该知道目标模式、群离席这些业务，从前每个功能各来一个静态口，越挂越多。
    ///
    /// 三个问题彼此独立，答案可以来自完全不同的两处（同一个会话既有壳又勾了自动同意）——
    /// 收成一个对象就是为了让「一次说清」有地方可去。
    /// </summary>
    public sealed class SessionWakePolicy
    {
        /// <summary>
        /// 这个会话的界面壳（按会话标识）：唤醒轮跑的是这个会话自己那一轮，它要动东西时
        /// 该弹给正看着它的人。没有壳为 null——再按 <see cref="UnattendedSource"/> 问无人值守，
        /// 两者都没有才按无头口径拒绝（与 <c>InProcessSchedulerBackend.DenyUnauthorizedApprovals</c> 同形）。
        ///
        /// 只认壳，不含无人值守：两条路的计数各归各的执行，共用一个通道会串限位
        /// </summary>
        public Func<string, ApprovalResolver?>? HostSource { get; init; }

        /// <summary>
        /// 这个会话的无人值守审批通道（按会话标识）：没有这种会话为 null。
        /// 每次调用都是新通道（计数从零起），调用方按执行持有、不得每批现取
        /// </summary>
        public Func<string, ApprovalResolver?>? UnattendedSource { get; init; }

        /// <summary>
        /// 无人值守决出的审批，在有壳时通知界面把卡片就地收掉（按会话标识取收卡回调）。
        /// 壳只负责显示、不参与判据——判据走 <see cref="UnattendedSource"/> 取到的本轮通道。
        /// 回调必须不抛、快返（Post 到 UI 线程即可）：抛了只记一条日志，不影响已决出的判据
        /// </summary>
        public Func<string, Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>>?>?
            SettledSource { get; init; }

        /// <summary>
        /// 这个会话有没有人看着这一跑。<b>与接不接得住审批是两件事</b>：无人值守的策略
        /// （目标模式的自动同意）接得住审批，但背后没有人。
        ///
        /// 按 resolver 推这一位就会把子代理的审批通道建起来——没人会去点，于是它白等 5 分钟
        /// 再按拒绝收口，而无人值守本该当场拒（见 <see cref="TurnDriver"/> 的 attended）。
        /// 缺省为“没人看着”：只有 <see cref="HostSource"/> 非空才是有人
        /// </summary>
        public Func<string, bool>? AttendedSource { get; init; }

        /// <summary>
        /// 目标模式的封顶豁免：某会话在目标模式里就不计「连续无用户参与的唤醒轮」。
        /// 目标模式无保险丝（模型自判结束或用户手动结束是仅有的出路），掐封顶等于悄悄结束它
        /// </summary>
        public Func<string, bool>? StreakExemptSource { get; init; }
    }

    /// <summary>唤醒轮的按会话策略；没注入口子上就没有壳、也没有豁免（测试与 CLI 走这条）</summary>
    public static SessionWakePolicy? Policy { get; set; }

    /// <summary>
    /// 用户在这个会话里说话了：自激封顶清零。
    ///
    /// 由 <see cref="TurnDriver.RunAsync"/> 在<b>带用户消息</b>的轮次上调——
    /// 「有没有用户参与」的唯一诚实判据就是这一轮有没有用户消息
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public static void NoteUserTurn(string? sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId)) _streaks.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// 起一轮唤醒轮。会话正忙就不起：要读的东西已经在历史里，模型下一轮自然读到。
    /// 连着太多轮没有用户参与也不起：结果同样已在历史里，用户下次说话时模型自然读到
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cause">起因，只进日志</param>
    /// <param name="cancellationToken">发起方撤回时取消（如目标模式结束），这一轮随之停下</param>
    public static async Task RunAsync(string sessionId, string cause, CancellationToken cancellationToken = default)
    {
        try
        {
            ChatSession? session = SessionManager.Instance.Load(sessionId);
            if (session == null) return;

            // 原子地占住这个会话:「查一下忙不忙,不忙就开跑」写成两步的话,查与开之间
            // 用户正好发一条,两轮就重叠了——它们共用会话本体、执行者与转录器
            using IDisposable? claim = SessionManager.Instance.Running.TryBeginRun(sessionId);
            if (claim == null)
            {
                Log.Debug($"Wake turn skipped: session={sessionId} busy; result already in history. cause={cause}");
                return;
            }

            // 目标模式豁免：不计连续无用户参与的唤醒轮（streak 恒 0，永不封顶）
            int streak = Policy?.StreakExemptSource?.Invoke(sessionId) == true
                ? 0
                : _streaks.AddOrUpdate(sessionId, 1, (_, n) => n + 1);
            if (streak > MaxConsecutiveWakeTurns)
            {
                Log.Warning($"Wake turn suppressed after {MaxConsecutiveWakeTurns} "
                            + $"consecutive turns without user input: session={sessionId} cause={cause}");
                return;
            }

            Log.Debug($"Wake turn started: session={sessionId} cause={cause} streak={streak}");
            // 无头驱动(sink 为 null),但**审批有人接**——就是这个会话自己那个窗口。
            // 这两件事必须分开告诉 TurnDriver:按 sink 推的话,共享的执行者会被标成
            // 「没人看着」,于是这一轮里派出的子代理连审批通道都不建(见 TurnDriver 的 attended)
            //
            // 有壳问壳（卡片弹给正看着它的人），无人值守走本轮自己的通道并另行通知壳收卡：
            // 两条路的计数各归各的执行，壳的通道只判用户点的那些，不替无人值守计数。
            // 壳与收卡回调每批现取：窗口可能在这一轮中途打开或关掉
            Func<ApprovalResolver?> hostSource = () => Policy?.HostSource?.Invoke(sessionId);
            ApprovalResolver? host = hostSource();
            UnattendedScope wakeScope = new(() => Policy?.UnattendedSource?.Invoke(sessionId));
            ApprovalResolver? resolver = ComposeWakeResolver(hostSource, wakeScope,
                () => Policy?.SettledSource?.Invoke(sessionId));
            // 「有没有人看着」只认壳,不按无人值守推:无人值守接得住审批,
            // 背后却没有人,按它推等于让子代理的审批白等 5 分钟再被拒；没注入口子默认没人看着
            bool attended = Policy?.AttendedSource?.Invoke(sessionId) ?? host != null;

            // 无宿主时收口正文写明「唤醒轮没窗口」,卡片据此解释「为什么没弹审批」;
            // 有宿主时审批卡在会话窗口里,按普通「无人回应」口径收口
            await SessionTurnRunner.RunHeadlessAsync(session, null, resolver, attended,
                unansweredResultText: host == null ? ToolCallCancellation.WakeUnansweredResultText : null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            //唤醒失败不影响结论:它已经在历史里了
            //记整个异常而不只 Message:堆栈是这类 NRE 唯一的定位依据(只记 Message 时无从查起)
            Log.Warning($"Wake turn failed: session={sessionId} cause={cause}: {e}");
        }
    }

    /// <summary>
    /// 唤醒轮本轮的审批回应口：无人值守开着走本轮自己的通道（计数不与壳串），关着问壳；
    /// 开跑时两边都没有为 null，口径与从前一致，驱动据此按无头收口。
    /// 开跑时只有壳也照样组合：开关可能在这一轮中途勾上
    /// </summary>
    /// <param name="hostSource">界面壳，每批现取；没有为 null</param>
    /// <param name="scope">本轮的无人值守作用域（开关现问、通道冻结）</param>
    /// <param name="settledSource">无人值守决出后通知壳收卡，每批现取；没有壳为 null</param>
    /// <returns>回应口；开跑时两边都没有为 null</returns>
    internal static ApprovalResolver? ComposeWakeResolver(Func<ApprovalResolver?> hostSource, UnattendedScope scope,
        Func<Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>>?> settledSource) =>
        UnattendedApprovals.Prefer(scope, hostSource,
            (requests, responses) => settledSource()?.Invoke(requests, responses));
}
