using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 播放与录音共用的一个 SoundFlow 引擎（miniaudio）。第一次出声或录音时才建：原生库加载与设备枚举不该压在启动上
/// </summary>
internal static class SoundFlowRuntime
{
    private static readonly Lock Gate = new();
    private static MiniAudioEngine? _engine;

    /// <summary>
    /// 引擎
    /// </summary>
    /// <exception cref="AudioDeviceException">原生库加载或后端初始化失败</exception>
    public static AudioEngine Engine
    {
        get
        {
            lock (Gate)
            {
                if (_engine != null) return _engine;
                try
                {
                    _engine = new MiniAudioEngine();
                }
                catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException
                                              or InvalidOperationException or TypeInitializationException)
                {
                    throw new AudioDeviceException($"Audio backend unavailable: {e.Message}", e);
                }

                return _engine;
            }
        }
    }

    /// <summary>
    /// 浮点格式
    /// </summary>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    /// <returns>格式</returns>
    public static AudioFormat FloatFormat(int sampleRate, int channels) => new()
    {
        Format = SampleFormat.F32,
        SampleRate = sampleRate,
        Channels = channels,
        Layout = AudioFormat.GetLayoutFromChannels(channels)
    };
}
