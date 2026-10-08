namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 音频 provider 的三分类（ADR 0071）。分类是术语的一部分：三类的用户可见代价互不相同——
/// 下几百兆、填密钥（或什么都不填）、什么都不用做。
/// </summary>
public enum EAudioProviderKind
{
    /// <summary>本地引擎：要下二进制与模型、有进程生命周期（audio.cpp）</summary>
    LocalEngine,

    /// <summary>在线服务：只发 HTTP，免密钥（Edge TTS）或要密钥（OpenAI 兼容）</summary>
    OnlineService,

    /// <summary>系统内置：操作系统自带，零下载零网络（macOS say）</summary>
    SystemBuiltIn
}

/// <summary>音频能力位：一个 provider 可以两种都有（audio.cpp 就是）</summary>
public enum EAudioCapability
{
    /// <summary>语音合成（TTS）</summary>
    Tts,

    /// <summary>语音识别（ASR）</summary>
    Asr
}

/// <summary>
/// 音频字节流的格式。各家的默认各不相同（OpenAI 默认 mp3、audio.cpp 默认 wav），
/// 播放器按这个决定怎么播，不猜。
/// </summary>
public enum EAudioFormat
{
    Wav,
    Mp3,
    Aiff,

    /// <summary>裸 PCM（s16le）。没有文件头，必须配 <see cref="AudioSynthesisResult.SampleRateHz"/> 才知道采样率</summary>
    Pcm16
}

/// <summary>一次合成请求</summary>
/// <param name="Text">要念的文本，不许为空</param>
/// <param name="Voice">provider 自己的音色 id（如 pocket 的 alba、Edge 的短名）；空 = provider 默认</param>
/// <param name="Speed">语速倍率；provider 不支持时显式忽略（契约里声明，不报错）</param>
/// <param name="Model">provider 自己的模型 id（试听某个非默认模型时用）；空 = provider 当前的默认</param>
/// <param name="Language">文字的主要语种（如 zh）；provider 可据此换一个会说这种语言的模型，没有这个本事的忽略</param>
public sealed record AudioSynthesisRequest(string Text, string? Voice = null, float? Speed = null, string? Model = null,
    string? Language = null);

/// <summary>一次合成结果：字节流 + 格式。流由调用方负责释放；Pcm16 时 <see cref="SampleRateHz"/> 必填</summary>
/// <param name="PlaybackSpeed">合成时没能变的语速，播放时补（变速不变调）；模型自己会变速的为 1</param>
public sealed record AudioSynthesisResult(Stream Audio, EAudioFormat Format, int? SampleRateHz = null,
    float PlaybackSpeed = 1f);

/// <summary>一次听写请求：音频流 + 格式。流由调用方负责释放</summary>
public sealed record AudioTranscriptionRequest(Stream Audio, EAudioFormat Format);

/// <summary>一次听写结果</summary>
public sealed record AudioTranscriptionResult(string Text);
