using System.Reflection;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

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
                  SaveUtility.LoadFromString<CharacterData>(Read(fileName))
                : SaveUtility.LoadFromString<CharacterData>(Read(fileName));

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

    internal string Read(string fileName)
    {
        var type = GetType();
        Assembly assembly = type.Assembly;

        var resourceName = $"{assembly.GetName().Name}.Resources.{fileName}";
        using Stream resource = assembly.GetManifestResourceStream(resourceName) ??
                                throw new FileNotFoundException($"Resource {fileName} not found.");
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd();
    }
}

public enum DefaultCharacter
{
    UserCard,
    Actor,
    RoleplaySimple,
    RoleplayDetailed,
    RoleplayImmersive,
    UiharuKazari,
    Expositor,
    ExpositorQuote,
    Translator,
    TranslatorAdvanced,
    TranslateReviwer,
    VisionOcr,
    Vision,
    Max
}