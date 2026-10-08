namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 出声或听写的那件东西（ADR 0071）。与文本侧的 <see cref="UiharuMind.Core.AI.Runtime.IModelRuntimeBackend"/>
/// 同构但互不隶属：TTS/ASR 是另一条能力轴，硬塞进聊天后端会让一个接口的语义分裂。
/// 每个实现都要能独立跑；降级路径写在拿 provider 的调用方契约里，provider 自己不依赖别的 provider。
/// </summary>
public interface IAudioProvider
{
    /// <summary>稳定 Id，也是设置里选中、注册表里查找用的那个</summary>
    string Id { get; }

    /// <summary>用户可见名</summary>
    string DisplayName { get; }

    /// <summary>本地引擎 / 在线服务 / 系统内置（ADR 0071 的三分类）</summary>
    EAudioProviderKind Kind { get; }

    /// <summary>能力位：Tts / Asr</summary>
    IReadOnlySet<EAudioCapability> Capabilities { get; }

    /// <summary>是否本地。本地引擎与系统内置都是 true；IsLocal 语义与文本侧一致，差别在 Kind</summary>
    bool IsLocal { get; }

    /// <summary>
    /// 合成。Text 为空抛 <see cref="ArgumentException"/>；Voice 为空用 provider 默认音色。
    /// 返回的流已就绪、可边读边播；释放归调用方。
    /// </summary>
    Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 听写。没有 Asr 能力的 provider 抛 <see cref="NotSupportedException"/>。
    /// 请求的流由调用方负责释放，provider 只读不关。
    /// </summary>
    Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 可选的音色 id；列不出的 provider 返回空，界面就只给默认
    /// </summary>
    Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
