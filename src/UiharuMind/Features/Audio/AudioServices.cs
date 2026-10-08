using System;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 语音的几件共享服务：组装入口一次递下去，免得每个页面挨个取
/// </summary>
/// <param name="LocalProvider">本地语音引擎的 provider（朗读、听写、语音模型页签共用一个，第一次用才建）</param>
/// <param name="Player">出声</param>
/// <param name="Recorder">录音</param>
/// <param name="ReadAloud">朗读（试听也走它，互相打断）</param>
public sealed record AudioServices(
    Func<AudioCppProvider> LocalProvider,
    IAudioPlayer Player,
    IAudioRecorder Recorder,
    IReadAloudService ReadAloud);
