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
            async ([Description("The words to speak (plain text; Markdown marks are not read).")]
                    string text,
                    [Description("Name of the character whose read-aloud voice to use. Omit for your own voice.")]
                    string? voice = null,
                    [Description("Optional tone, e.g. \"calm narration\". Only some voices follow it.")]
                    string? tone = null,
                    [Description("Optional speed multiplier between 0.5 and 2. Omit for the voice's usual speed.")]
                    float? speed = null,
                    CancellationToken cancellationToken = default) =>
                await SynthesizeAsync(paths, self, synthesizer, characters, text, voice, tone, speed, cancellationToken)
                    .ConfigureAwait(false),
            ToolName,
            "Turn text into a speech audio file (WAV) in a character's voice, e.g. for voice-overs. " +
            "Nothing is played aloud. For several speakers, call once per line and join the files yourself.");
    }

    private static async Task<string> SynthesizeAsync(AgentPathResolver paths, CharacterData self,
        SpeechFileSynthesizer synthesizer, Func<IEnumerable<CharacterData>> characters, string text, string? voice,
        string? tone, float? speed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Error: text must not be empty.";
        CharacterData? speaker = string.IsNullOrWhiteSpace(voice) ? self : Find(characters(), voice.Trim());
        if (speaker == null)
        {
            string known = string.Join(", ", characters().Where(x => x.Speech is { IsEmpty: false })
                .Select(x => x.CharacterName).Where(x => x.Length > 0).Distinct().Order(StringComparer.Ordinal));
            return $"Error: no character named '{voice}'." +
                   (known.Length > 0 ? $" Characters with a voice set: {known}." : " No character has a voice set yet.");
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
            return $"Saved: {paths.ToPortable(file.Path)} ({file.Duration.TotalSeconds:0.0} s, voice of {speaker.CharacterName}).";
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return $"Error: speech synthesis failed: {e.Message}\nTell the user to check the voice settings.";
        }
    }

    // 按显示名找，也认 id（名字可能重，取第一个）
    private static CharacterData? Find(IEnumerable<CharacterData> characters, string name) =>
        characters.FirstOrDefault(x => string.Equals(x.CharacterName, name, StringComparison.OrdinalIgnoreCase)) ??
        characters.FirstOrDefault(x => string.Equals(x.CharacterId, name, StringComparison.OrdinalIgnoreCase));
}
