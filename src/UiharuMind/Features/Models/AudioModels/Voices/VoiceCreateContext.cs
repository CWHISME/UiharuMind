using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Voices;

/// <summary>
/// 能抽卡的模型：会克隆、不靠参考音频也念得出来、没有自带音色
/// </summary>
/// <param name="Id">模型 id</param>
/// <param name="DisplayName">家族显示名</param>
public sealed record VoiceDrawModel(string Id, string DisplayName);

/// <summary>
/// 添加音色要用到的东西，由组装入口备齐
/// </summary>
/// <param name="Library">音色库</param>
/// <param name="Player">试听</param>
/// <param name="Recorder">录音</param>
/// <param name="Messages">提示</param>
/// <param name="DrawModels">能抽卡的模型</param>
/// <param name="Draw">抽卡：模型 id、随机种子 → 抽到的音色</param>
/// <param name="Transcribe">把参考音频识别成文字；本机没有识别模型为 null</param>
/// <param name="ReadFile">读一个音频文件，转成参考音频的格式</param>
/// <param name="UseForModel">把音色设成某个模型的音色：模型 id、音色名</param>
/// <param name="Post">切回界面线程</param>
public sealed record VoiceCreateContext(
    VoiceLibrary Library,
    IAudioPlayer Player,
    IAudioRecorder Recorder,
    IMessageService Messages,
    IReadOnlyList<VoiceDrawModel> DrawModels,
    Func<string, int, CancellationToken, Task<VoiceDraft>> Draw,
    Func<byte[], CancellationToken, Task<string>>? Transcribe,
    Func<string, AudioRecording> ReadFile,
    Action<string, string> UseForModel,
    Action<Action> Post)
{
    /// <summary>
    /// 参考音频的采样率：克隆模型多在 16k~24k 上取特征，录高一点免得声音发闷
    /// </summary>
    public const int SampleRate = 24000;

    /// <summary>
    /// 导入时最多取多长：参考音频太长，克隆模型反而念不稳
    /// </summary>
    public static readonly TimeSpan MaxImportDuration = TimeSpan.FromSeconds(20);
}
