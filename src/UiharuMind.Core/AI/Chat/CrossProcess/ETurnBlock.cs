namespace UiharuMind.Core.AI.Chat.CrossProcess;

/// <summary>
/// 这个会话此刻为什么不能在本实例里开跑（ADR 0064）
/// </summary>
public enum ETurnBlock
{
    /// <summary>能跑</summary>
    None,

    /// <summary>别的实例改过它，本实例内存里那份已旧</summary>
    StaleCopy,

    /// <summary>它正在别的实例里跑</summary>
    RunningElsewhere,
}
