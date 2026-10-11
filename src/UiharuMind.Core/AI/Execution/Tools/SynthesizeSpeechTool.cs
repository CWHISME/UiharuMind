using System.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 配音工具：把一段文字用某个角色的「朗读声音」合成成音频文件，存进草稿目录的 audio 下。
/// 声音按角色名点（不点用自己的），多角色配音就逐句换角色合成、再自行拼接。与 Speak 不同：不出声、只出文件
/// </summary>
public static class SynthesizeSpeechTool
{
    /// <summary>工具名</summary>
    public const string ToolName = "SynthesizeSpeech";

    /// <summary>草稿目录下放配音的子目录</summary>
    public const string AudioFolder = "audio";

    // 结果里文件那一行，工具卡据此找音频；写与读都在本类
    private const string SavedPrefix = "Saved: ";

    /// <summary>
    /// 创建配音 AIFunction
    /// </summary>
    /// <param name="paths">路径口径（存到草稿目录、返回可移植路径）</param>
    /// <param name="self">自己（不点角色时用它的声音）</param>
    /// <returns>工具实例</returns>
    public static AITool Create(AgentPathResolver paths, CharacterData self) =>
        Create(paths, self, SpeechFileSynthesizer.Shared, () => CharacterManager.Instance.CharacterDataDictionary.Values);

    internal static AITool Create(AgentPathResolver paths, CharacterData self, SpeechFileSynthesizer synthesizer,
        Func<IEnumerable<CharacterData>> characters)
    {
        return AIFunctionFactory.Create(
            async ([Description("要合成的文字（纯文本；不识别 Markdown 标记）。")]
                    string text,
                    [Description("使用哪个角色的朗读声音。省略则用你自己的声音。")]
                    string? voice = null,
                    [Description("可选语气，如「平静的旁白」。只有部分声音会遵循。")]
                    string? tone = null,
                    [Description("可选的语速倍数，0.5 到 2 之间。省略则用该声音的默认语速。")]
                    float? speed = null,
                    CancellationToken cancellationToken = default) =>
                await SynthesizeAsync(paths, self, synthesizer, characters, text, voice, tone, speed, cancellationToken)
                    .ConfigureAwait(false),
            ToolName,
            "把文字用某个角色的声音合成为语音音频文件（WAV），比如用于配音。" +
            "不会出声播放。多角色配音时逐句调用一次、自行拼接文件。");
    }

    /// <summary>
    /// 从结果里取出存好的文件（工具卡上放音用）
    /// </summary>
    /// <param name="result">工具结果原文</param>
    /// <returns>可移植路径，按出现顺序</returns>
    public static IReadOnlyList<string> ParseSavedPaths(string? result) => ToolResultLines.Parse(result, SavedPrefix);

    private static async Task<string> SynthesizeAsync(AgentPathResolver paths, CharacterData self,
        SpeechFileSynthesizer synthesizer, Func<IEnumerable<CharacterData>> characters, string text, string? voice,
        string? tone, float? speed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Error: 文本不能为空。";
        CharacterData? speaker = string.IsNullOrWhiteSpace(voice) ? self : Find(characters(), voice.Trim());
        if (speaker == null)
        {
            string known = string.Join(", ", characters().Where(x => x.Speech is { IsEmpty: false })
                .Select(x => x.CharacterName).Where(x => x.Length > 0).Distinct().Order(StringComparer.Ordinal));
            return $"Error: 没有名为 '{voice}' 的角色。" +
                   (known.Length > 0 ? $" 已设置声音的角色：{known}。" : " 还没有任何角色设置声音。");
        }

        SpeechStyle style = speaker.Speech ?? new SpeechStyle();
        if (!string.IsNullOrWhiteSpace(tone)) style = style with { Instruction = tone.Trim() };
        if (speed is { } rate) style = style with
        {
            Speed = Math.Clamp(rate, AudioSettingConfig.MinSpeed, AudioSettingConfig.MaxSpeed)
        };

        string directory = AgentOutputLayout.EnsureDraftSubdirectory(paths.DraftRoot, AudioFolder);
        string stem = ImageGenerationTool.BuildFileStem(text, DateTime.Now);
        string path = Path.Combine(directory, stem + ".wav");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{stem}-{i}.wav");
        try
        {
            SpeechFile file = await synthesizer.SynthesizeAsync(text, style, path, cancellationToken).ConfigureAwait(false);
            // 工具卡上能直接放，正文不必再贴路径
            return $"{SavedPrefix}{paths.ToPortable(file.Path)}\n" +
                   $"{file.Duration.TotalSeconds:0.0} 秒，{speaker.CharacterName} 的声音。" +
                   "用户可以在本工具卡片上播放；除非被问起，不必重复路径。";
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return $"Error: 语音合成失败：{e.Message}\n告诉用户检查声音设置。";
        }
    }

    // 按显示名找，也认 id（名字可能重，取第一个）
    private static CharacterData? Find(IEnumerable<CharacterData> characters, string name) =>
        characters.FirstOrDefault(x => string.Equals(x.CharacterName, name, StringComparison.OrdinalIgnoreCase)) ??
        characters.FirstOrDefault(x => string.Equals(x.CharacterId, name, StringComparison.OrdinalIgnoreCase));
}
