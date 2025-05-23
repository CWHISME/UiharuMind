using System.Reflection;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character;

public class DefaultCharacterManager : Singleton<DefaultCharacterManager>, IInitialize
{
    public readonly Dictionary<DefaultCharacter, CharacterData> Characters =
        new Dictionary<DefaultCharacter, CharacterData>();

    public void OnInitialize()
    {
        const int max = (int)DefaultCharacter.Max;
        for (int i = 0; i < max; i++)
        {
            var character = (DefaultCharacter)i;
            var characterName = character.ToString();

            string fileName = characterName + ".json";
            string externalFileName = Path.Combine(SettingConfig.SaveDefaultCharacterDataPath, fileName);
            CharacterData characterData = File.Exists(externalFileName)
                ? SaveUtility.Load<CharacterData>(externalFileName) ??
                  EmbeddedResourcesUtils.ReadFromJson<CharacterData>(fileName)
                : EmbeddedResourcesUtils.ReadFromJson<CharacterData>(fileName);

            characterData.IsDefaultCharacter = true;
            Characters.Add(character, characterData);
        }
    }

    /// <summary>
    /// 获取一个默认角色的数据
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public CharacterData GetCharacterData(DefaultCharacter character)
    {
        return Characters[character];
    }
}

public enum DefaultCharacter
{
    /// <summary>
    /// 空角色，用于找不到的情况下默认角色
    /// </summary>
    Empty,
    /// <summary>
    /// 用户角色卡
    /// </summary>
    UserCard,
    Roleplay_FirstPerson,
    Roleplay_ThirdPerson,
    // RoleplayDetailed,
    // RoleplayImmersive,
    UiharuKazari,

    /// <summary>
    /// 本地语言
    /// </summary>
    SystemLanguage,

    /// <summary>
    /// 默认助手
    /// </summary>
    Assistant,

    /// <summary>
    /// 解释内容
    /// </summary>
    AssistantExplain,

    /// <summary>
    /// 高级专家
    /// </summary>
    AssistantExpert,

    /// <summary>
    /// 高级专家的(带额外引用信息)
    /// </summary>
    AssistantExpertQuote,

    /// <summary>
    /// 思维链
    /// </summary>
    ChainofThought,
    Translator,
    TranslatorAdvanced,
    TranslateReviwer,
    VisionOcr,
    Vision,
    Max
}