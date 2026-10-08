using System.Collections.Generic;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Options;

/// <summary>
/// 参数的显示名与说明。常用参数按名字给中文（同名参数在各家模型里含义一致），其余用参数名与上游英文说明
/// </summary>
/// <param name="Label">显示名</param>
/// <param name="Note">说明</param>
public sealed record AudioOptionText(string Label, string Note)
{
    private static readonly Dictionary<string, (LangKey Label, LangKey Note)> Common = new()
    {
        ["seed"] = (LangKey.AudioOptionSeed, LangKey.AudioOptionSeedNote),
        ["temperature"] = (LangKey.AudioOptionTemperature, LangKey.AudioOptionTemperatureNote),
        ["top_p"] = (LangKey.AudioOptionTopP, LangKey.AudioOptionTopPNote),
        ["top_k"] = (LangKey.AudioOptionTopK, LangKey.AudioOptionTopKNote),
        ["num_inference_steps"] = (LangKey.AudioOptionSteps, LangKey.AudioOptionStepsNote),
        ["guidance_scale"] = (LangKey.AudioOptionGuidance, LangKey.AudioOptionGuidanceNote),
        ["cfg_strength"] = (LangKey.AudioOptionGuidance, LangKey.AudioOptionGuidanceNote),
        ["repetition_penalty"] = (LangKey.AudioOptionRepetition, LangKey.AudioOptionRepetitionNote),
        ["max_tokens"] = (LangKey.AudioOptionMaxTokens, LangKey.AudioOptionMaxTokensNote),
        ["text_chunk_size"] = (LangKey.AudioOptionChunkSize, LangKey.AudioOptionChunkSizeNote),
        ["num_beams"] = (LangKey.AudioOptionBeams, LangKey.AudioOptionBeamsNote),
        ["context"] = (LangKey.AudioOptionContext, LangKey.AudioOptionContextNote),
        ["hotwords"] = (LangKey.AudioOptionHotwords, LangKey.AudioOptionHotwordsNote)
    };

    /// <summary>
    /// 某个参数的显示名与说明
    /// </summary>
    /// <param name="option">spec 声明的参数</param>
    /// <returns>显示名与说明</returns>
    public static AudioOptionText For(AudioCppOption option) =>
        Common.TryGetValue(option.Name, out var keys)
            ? new AudioOptionText(Loc.Text(keys.Label), Loc.Text(keys.Note))
            : new AudioOptionText(option.Name, option.Description);
}
