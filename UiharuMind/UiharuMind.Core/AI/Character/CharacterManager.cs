using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Character;

public class CharacterManager : Singleton<CharacterManager>, IInitialize
{
    public readonly List<CharacterData> CharacterDataList = new List<CharacterData>();

    public void OnInitialize()
    {
        var files = Directory.GetFiles(SettingConfig.SaveCharacterDataPath, "*.json");
        foreach (var file in files)
        {
            var characterData = SaveUtility.Load<CharacterData>(file);
            CharacterDataList.Add(characterData);
        }

        if (CharacterDataList.Count == 0) CharacterDataList.Add(DefaultCharacter.CreateDefalutCharacter());
    }

    public void SaveCharacterData(CharacterData characterData)
    {
        var savePath = Path.Combine(SettingConfig.SaveCharacterDataPath, characterData.CharacterName + ".json");
        SaveUtility.SaveToPath(savePath, characterData);
        CharacterDataList.Add(characterData);
    }
}