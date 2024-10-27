using System.Reflection;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Character;

public class CharacterManager : Singleton<CharacterManager>, IInitialize
{
    public readonly Dictionary<string, CharacterData> CharacterDataDictionary = new Dictionary<string, CharacterData>();


    /// <summary>
    /// 用户角色的名字
    /// </summary>
    public string UserCharacterName => _userCharacterData!.CharacterName;

    /// <summary>
    /// 代表用户自己的角色数据
    /// </summary>
    public CharacterData UserCharacterData => _userCharacterData!;

    private CharacterData? _userCharacterData;

    public void OnInitialize()
    {
        var files = Directory.Exists(SettingConfig.SaveCharacterDataPath)
            ? Directory.GetFiles(SettingConfig.SaveCharacterDataPath, "*.json")
            : null;

        if (files != null)
        {
            foreach (var file in files)
            {
                var characterData = SaveUtility.Load<CharacterData>(file);
                if (characterData != null) CharacterDataDictionary.Add(characterData.CharacterName, characterData);
            }
        }

        if (CharacterDataDictionary.Count == 0)
        {
            var defaultCharacter = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.UiharuKazari);
            CharacterDataDictionary.Add(defaultCharacter.CharacterName, defaultCharacter);
        }

        //CharacterDataList.Add(DefaultCharacter.CreateDefalutCharacter());
        // string yaml = Read("UiharuKazari.yaml");
        // Log.Debug(yaml);
        // var str = SaveUtility.SaveToString(CharacterDataList[0]);
        // var char2 = SaveUtility.LoadFromString<CharacterData>(str);
        // var yamlStr = YamlUtility.SaveToString(CharacterDataList[0]);
        // var char22=YamlUtility.LoadFromString<CharacterData>(yamlStr);
        _userCharacterData = new CharacterData
        {
            CharacterName = "桃子",
            Description = "桃子是来自学园都市的黑猫，喜欢甜食和在树荫下晒太阳，希望有人可以与她聊天，如果被拒绝了会很悲伤。",
        };
    }

    /// <summary>
    /// 获取角色，如果不存在，则返回默认角色
    /// </summary>
    /// <param name="characterName"></param>
    /// <returns></returns>
    public CharacterData GetCharacterData(string characterName)
    {
        if (CharacterDataDictionary.TryGetValue(characterName, out var characterData)) return characterData;
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.UiharuKazari);
    }
    
    public void SaveCharacterData(CharacterData characterData)
    {
        var savePath = Path.Combine(SettingConfig.SaveCharacterDataPath, characterData.CharacterName + ".json");
        SaveUtility.Save(savePath, characterData);
        CharacterDataDictionary.Add(characterData.CharacterName, characterData);
    }
}