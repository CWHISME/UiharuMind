namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群聊的调度模式（ADR 0049 决策 1）。与群类型是两根轴；运行中可切换，下一波起生效
/// </summary>
public enum EGroupScheduleMode
{
    /// <summary>串行：成员按顺序一人一轮，一圈即停</summary>
    Serial,

    /// <summary>并行：被唤醒的成员并发开跑，发言即时广播给正在跑的成员</summary>
    Parallel,
}
