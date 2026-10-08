using System;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 按角色取它的朗读声音。声音指的在线服务删了、本地模型没了，就只留语速与语气、其余跟默认，
/// 免得因为一处旧设置念不出来
/// </summary>
public sealed class CharacterSpeechStyles
{
    private readonly Func<string, CharacterData?> _characters;
    private readonly Func<string, bool> _hasService;
    private readonly Func<string, bool> _hasLocalModel;

    /// <param name="characters">角色 id → 角色</param>
    /// <param name="hasService">在线服务 id 还在、能用</param>
    /// <param name="hasLocalModel">本地模型 id 还在</param>
    public CharacterSpeechStyles(Func<string, CharacterData?> characters, Func<string, bool> hasService,
        Func<string, bool> hasLocalModel)
    {
        _characters = characters;
        _hasService = hasService;
        _hasLocalModel = hasLocalModel;
    }

    /// <summary>
    /// 这个角色的声音
    /// </summary>
    /// <param name="characterId">角色 id；null 为不是角色说的</param>
    /// <returns>声音；没设或不是角色为 null</returns>
    public SpeechStyle? For(string? characterId)
    {
        if (characterId == null || _characters(characterId)?.Speech is not { IsEmpty: false } style) return null;
        bool stale = style.Provider switch
        {
            null => false,
            AudioCppProvider.LocalId => style.Model != null && !_hasLocalModel(style.Model),
            _ => !_hasService(style.Provider)
        };
        return stale ? style with { Provider = null, Model = null, Voice = null } : style;
    }
}
