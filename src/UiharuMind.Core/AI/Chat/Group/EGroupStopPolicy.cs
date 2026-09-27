namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 并行群聊的停止条件（ADR 0049 决策 6）。串行不看它
/// </summary>
public enum EGroupStopPolicy
{
    /// <summary>保守：成员发言引起的唤醒只传一跳，被这样叫醒的人说完不再叫醒任何人</summary>
    Conservative,

    /// <summary>激进：不设防护，聊到自然停或用户手动停</summary>
    Aggressive,
}
