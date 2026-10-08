using System;
using System.Net.Http;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 语音相关失败的提示文案：引擎没装、模型不在、内存保护拒绝、没有音频设备……试听、朗读、听写共用
/// </summary>
public static class AudioErrorText
{
    /// <summary>
    /// 是不是语音链路上预期内的失败（该提示用户，而不是当程序错误）
    /// </summary>
    /// <param name="e">异常</param>
    /// <returns>是为 true</returns>
    public static bool IsExpected(Exception e) => e is LocalEngineNotReadyException or LocalServerException
        or AudioModelMissingException or AudioVoiceRequiredException or AudioDeviceException or HttpRequestException
        or NotSupportedException;

    /// <summary>
    /// 给用户看的一句话
    /// </summary>
    /// <param name="e">异常</param>
    /// <returns>文案</returns>
    public static string Describe(Exception e) => e switch
    {
        LocalEngineNotReadyException => Loc.Text(LangKey.AudioErrorNoEngine),
        LocalServerException server => Loc.Text(LangKey.AudioErrorServer, server.Detail),
        AudioModelMissingException missing => Loc.Text(missing.Task == EAudioCppTask.Tts
            ? LangKey.AudioErrorNoTtsModel
            : LangKey.AudioErrorNoAsrModel),
        AudioVoiceRequiredException => Loc.Text(LangKey.AudioErrorVoiceRequired),
        AudioServiceException { ErrorType: AudioServiceException.InsufficientMemory } memory =>
            Loc.Text(LangKey.AudioErrorMemory, memory.Message),
        AudioDeviceException device => Loc.Text(LangKey.AudioErrorDevice, device.Message),
        _ => e.Message
    };
}
