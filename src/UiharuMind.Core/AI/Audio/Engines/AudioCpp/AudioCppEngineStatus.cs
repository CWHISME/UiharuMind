namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 跑着的语音引擎里装了什么（进程在不在跑看 <see cref="AudioCppEngine.Runtime"/>）
/// </summary>
/// <param name="Backend">在用的后端（metal / cpu…）；没在跑为 null</param>
/// <param name="LoadedModels">已加载进内存的模型 id</param>
public sealed record AudioCppEngineStatus(string? Backend, IReadOnlySet<string> LoadedModels)
{
    /// <summary>
    /// 没在跑
    /// </summary>
    public static AudioCppEngineStatus Stopped { get; } = new(null, new HashSet<string>());
}
