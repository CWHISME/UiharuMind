using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Characters.Voice;

/// <summary>
/// 角色声音的「用哪个」：跟默认、某个本地合成模型、某个在线朗读服务
/// </summary>
/// <param name="Provider">provider id；null 跟默认</param>
/// <param name="Model">本地模型 id</param>
/// <param name="Name">显示名</param>
/// <param name="Detail">补充说明（模型 id、「在线」）</param>
/// <param name="KnownVoices">不用联网、不用起引擎就知道的音色（音色库、spec 列的自带音色、预设音色）</param>
/// <param name="AcceptsInstruction">听语气描述</param>
/// <param name="IsLocal">本地模型：要列自带音色得拉起引擎，交给用户点</param>
public sealed record CharacterVoiceSource(
    string? Provider,
    string? Model,
    string Name,
    string Detail,
    IReadOnlyList<string> KnownVoices,
    bool AcceptsInstruction,
    bool IsLocal)
{
    /// <summary>
    /// 跟默认：朗读默认用哪个就用哪个，音色由它定
    /// </summary>
    public static CharacterVoiceSource Default { get; } = new(null, null, Loc.Text(LangKey.CharacterVoiceFollowDefault),
        "", [], true, false);

    public bool IsDefault => Provider == null;

    /// <summary>
    /// 本机能选的全部：跟默认、各本地合成模型、各在线朗读服务
    /// </summary>
    /// <param name="engine">语音引擎</param>
    /// <param name="services">在线朗读服务</param>
    /// <param name="library">音色库</param>
    /// <returns>选项，「跟默认」在最前</returns>
    public static IReadOnlyList<CharacterVoiceSource> All(AudioCppEngine engine, IEnumerable<AudioServiceInfo> services,
        VoiceLibrary library)
    {
        List<CharacterVoiceSource> result = [Default];
        string[] libraryVoices = library.List().Select(x => x.Name).ToArray();
        foreach (AudioCppModelEntry model in engine.Models.GetModels().Where(x => x.Task == EAudioCppTask.Tts.ToConfigName()))
        {
            AudioCppFamily? family = engine.Models.GetFamily(model);
            IEnumerable<string> voices = (family?.CanClone == true ? libraryVoices : [])
                .Concat(family?.BuiltinVoices ?? []);
            result.Add(new CharacterVoiceSource(AudioCppProvider.LocalId, model.Id, family?.DisplayName ?? model.Id,
                model.Id, voices.Distinct().ToList(), family?.InstructionOption != null, true));
        }

        foreach (AudioServiceInfo service in services.Where(x => x is { Task: EAudioCapability.Tts, IsConfigured: true }))
        {
            IReadOnlyList<string> voices = service.Dialect == EAudioDialect.EdgeTts
                ? []
                : AudioServicePreset.Match(service).Voices.Append(service.Voice).Where(x => x.Length > 0).Distinct().ToList();
            result.Add(new CharacterVoiceSource(service.Id, null, service.Name, Loc.Text(LangKey.AudioServiceOnline),
                voices, service.AcceptsInstructions, false));
        }

        return result;
    }

    /// <summary>
    /// 角色卡上存的那个已经不在了（服务删了、模型删了）：占个位显示出来，不悄悄换掉
    /// </summary>
    /// <param name="style">存的声音</param>
    /// <returns>占位项</returns>
    public static CharacterVoiceSource Missing(SpeechStyle style) => new(style.Provider, style.Model,
        Loc.Text(LangKey.CharacterVoiceMissing, style.Model ?? style.Provider ?? ""), "", [], true,
        style.Provider == AudioCppProvider.LocalId);

    /// <summary>
    /// 是不是指这一项
    /// </summary>
    /// <param name="style">存的声音</param>
    /// <returns>是为 true</returns>
    public bool Matches(SpeechStyle? style) => style?.Provider == Provider &&
                                               (Provider != AudioCppProvider.LocalId || style?.Model == Model);
}
