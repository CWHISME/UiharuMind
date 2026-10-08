namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 语音引擎进程的状态
/// </summary>
public enum EAudioEngineRunState
{
    Stopped,
    Starting,
    Running
}

/// <summary>
/// 语音引擎此刻的状态
/// </summary>
/// <param name="State">进程状态</param>
/// <param name="Backend">在用的后端（metal / cpu…）；没起过为 null</param>
/// <param name="LoadedModels">已加载进内存的模型 id</param>
public sealed record AudioCppEngineStatus(EAudioEngineRunState State, string? Backend, IReadOnlySet<string> LoadedModels)
{
    /// <summary>
    /// 没在跑
    /// </summary>
    public static AudioCppEngineStatus Stopped { get; } = new(EAudioEngineRunState.Stopped, null, new HashSet<string>());
}
