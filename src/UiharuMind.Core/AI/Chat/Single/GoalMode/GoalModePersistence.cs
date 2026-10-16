using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>进行中目标模式的存档形态（可序列化）</summary>
/// <param name="SessionId">会话标识</param>
/// <param name="Goal">目标文本</param>
/// <param name="Rules">调度规则</param>
/// <param name="StartedAt">开始时刻</param>
/// <param name="Turns">已跑轮数</param>
/// <param name="ArtifactStamp">最近的产物指纹</param>
/// <param name="LastStop">最近一次停下的时刻</param>
/// <param name="BackoffLevel">连续没进展的次数</param>
/// <param name="AutoApproveApprovals">唤醒轮的审批是否自动同意（无人值守时直接放行）</param>
public sealed record GoalModeState(string SessionId, string Goal, IReadOnlyList<GoalModeScheduleRule> Rules,
    DateTimeOffset StartedAt, int Turns, string ArtifactStamp, DateTimeOffset LastStop, int BackoffLevel,
    bool AutoApproveApprovals = false);

/// <summary>
/// 目标模式的存档读写（仿 <see cref="AI.Execution.Tools.Scheduler.InProcessSchedulerBackend"/>）：
/// 进行中的目标模式落一个 JSON，应用重启后恢复，继续按调度跑
/// </summary>
public static class GoalModePersistence
{
    /// <summary>读存档；没有或读不出为空表</summary>
    /// <returns>进行中的目标模式</returns>
    public static IReadOnlyList<GoalModeState> Load()
    {
        return SaveUtility.Load<List<GoalModeState>>(AppPaths.Data.GoalModeSessions, SaveUtility.SaveJsonOptions) ?? [];
    }

    /// <summary>整份覆盖写</summary>
    /// <param name="states">进行中的目标模式</param>
    public static void Save(IReadOnlyList<GoalModeState> states)
    {
        SaveUtility.Save(AppPaths.Data.GoalModeSessions, states, SaveUtility.SaveJsonOptions);
    }
}
