namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 请求参数的值类型（spec 的 type）
/// </summary>
public enum EAudioCppOptionType
{
    /// <summary>string</summary>
    Text,

    /// <summary>int</summary>
    Integer,

    /// <summary>float</summary>
    Number,

    /// <summary>bool</summary>
    Toggle,

    /// <summary>enum</summary>
    Choice,

    /// <summary>path、audio_path、string_list 等：界面不让填</summary>
    Other
}

/// <summary>
/// 模型 spec 声明的一个请求参数（options.request 的一项）。值一律按字符串发给 server，由它按类型解析
/// </summary>
/// <param name="Name">参数名，如 temperature</param>
/// <param name="Type">值类型</param>
/// <param name="Description">上游说明（英文原文）</param>
/// <param name="Default">默认值（invariant 格式；布尔为 true/false）；spec 没给为 null，即由模型自己定</param>
/// <param name="Min">下限；没给为 null</param>
/// <param name="Max">上限；没给为 null</param>
/// <param name="Values">可选值（Choice 才有；只给了上游预设名的为空）</param>
public sealed record AudioCppOption(
    string Name,
    EAudioCppOptionType Type,
    string Description,
    string? Default,
    double? Min,
    double? Max,
    IReadOnlyList<string> Values);
