using System.Text.Json.Serialization;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 怎么念：用哪个、什么音色、多快、什么语气。与具体模型无关，provider 按自己会的取用，不会的忽略。
/// 角色卡上存一份（角色专属声音）；试听、以后智能体自己朗读也用它
/// </summary>
public sealed record SpeechStyle
{
    /// <summary>用哪个：本地引擎（<see cref="AudioCppProvider.LocalId"/>）或在线服务（service:名字）；null 跟默认</summary>
    public string? Provider { get; init; }

    /// <summary>本地模型 id；null 由引擎按语种挑</summary>
    public string? Model { get; init; }

    /// <summary>音色（本地的音色库 / 自带音色名，在线服务的音色 id）；null 为默认</summary>
    public string? Voice { get; init; }

    /// <summary>语速倍率；null 跟全局设置</summary>
    public float? Speed { get; init; }

    /// <summary>语气描述（如「低声、有点难过」）；只有会听指令的模型用</summary>
    public string? Instruction { get; init; }

    /// <summary>什么都没设</summary>
    [JsonIgnore]
    public bool IsEmpty => Provider == null && Model == null && Voice == null && Speed == null &&
                           string.IsNullOrWhiteSpace(Instruction);
}
