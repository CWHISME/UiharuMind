/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using UiharuMind.Core.AI.Chat;
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
    /// 唤醒轮的审批回应通道从哪儿取（按会话标识）。
    ///
    /// 由界面注入：唤醒轮跑的是这个会话自己那一轮，它要动东西时该弹给正看着它的人。
    /// 取不到就按无头口径拒绝——与 <c>InProcessSchedulerBackend.DenyUnauthorizedApprovals</c> 同形。
    /// </summary>
    public static Func<string, ApprovalResolver?>? ApprovalSource { get; set; }

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
    public static async Task RunAsync(string sessionId, string cause)
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

            int streak = _streaks.AddOrUpdate(sessionId, 1, (_, n) => n + 1);
            if (streak > MaxConsecutiveWakeTurns)
            {
                Log.Warning($"Wake turn suppressed after {MaxConsecutiveWakeTurns} "
                            + $"consecutive turns without user input: session={sessionId} cause={cause}");
                return;
            }

            Log.Debug($"Wake turn started: session={sessionId} cause={cause} streak={streak}");
            // 整轮同一实例：Attach 与 Run 共用租约里的那一个，轮次中途的释放只延迟、不换实例
            using ChatSession.RunnerLease lease = await session.AcquireRunnerAsync().ConfigureAwait(false);
            using TurnDriver driver = new(null, new TurnUsageLedger());
            // 无头驱动(sink 为 null),但**审批有人接**——就是这个会话自己那个窗口。
            // 这两件事必须分开告诉 TurnDriver:按 sink 推的话,共享的执行者会被标成
            // 「没人看着」,于是这一轮里派出的子代理连审批通道都不建(见 TurnDriver 的 attended)
            ApprovalResolver? resolver = ApprovalSource?.Invoke(sessionId);
            await driver.RunAsync(session, lease.Runner, null, resolver, attended: resolver != null)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            //唤醒失败不影响结论:它已经在历史里了
            Log.Warning($"Wake turn failed: session={sessionId} cause={cause}: {e.Message}");
        }
    }
}
