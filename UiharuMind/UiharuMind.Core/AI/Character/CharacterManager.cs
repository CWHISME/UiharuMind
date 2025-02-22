using System.Reflection;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Character;

public class CharacterManager : Singleton<CharacterManager>, IInitialize
{
    public readonly Dictionary<string, CharacterData> CharacterDataDictionary = new Dictionary<string, CharacterData>();

    public event Action<CharacterData>? OnCharacterAdded;
    public event Action<CharacterData>? OnCharacterRemoved;

    /// <summary>
    /// 用户角色的名字
    /// </summary>
    public string UserCharacterName => UserCharacterData.Description;

    /// <summary>
    /// 代表用户自己的角色数据
    /// Description: 代表名字
    /// Template: 代表描述模板
    /// </summary>
    public CharacterData UserCharacterData =>
        DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.UserCard);

    // private CharacterData? _userCharacterData;

    public void OnInitialize()
    {
        var files = Directory.Exists(SettingConfig.SaveCharacterDataPath)
            ? Directory.GetFiles(SettingConfig.SaveCharacterDataPath, "*.json", SearchOption.AllDirectories)
            : null;

        if (files != null)
        {
            foreach (var file in files)
            {
                var characterData = SaveUtility.Load<CharacterData>(file);
                if (characterData != null)
                {
                    try
                    {
                        characterData.FileDateTime = File.GetLastWriteTime(file).ToFileTimeUtc();
                    }
                    catch (Exception e)
                    {
                        Log.Error(e);
                    }

                    if (string.IsNullOrEmpty(characterData.CharacterName))
                        characterData.CharacterName = Path.GetFileNameWithoutExtension(file);
                    CharacterDataDictionary.Add(characterData.CharacterName, characterData);
                }
            }
        }

        //装载默认角色
        foreach (var defCharacter in DefaultCharacterManager.Instance.Characters)
        {
            if (CharacterDataDictionary.ContainsKey(defCharacter.Value.CharacterName)) continue;
            if (defCharacter.Value.IsHide) continue;
            CharacterDataDictionary.Add(defCharacter.Value.CharacterName, defCharacter.Value);
        }

        // if (CharacterDataDictionary.Count == 0)
        // {
        //     var defaultCharacter = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.UiharuKazari);
        //     CharacterDataDictionary.Add(defaultCharacter.CharacterName, defaultCharacter);
        // }

        //CharacterDataList.Add(DefaultCharacter.CreateDefalutCharacter());
        // string yaml = Read("UiharuKazari.yaml");
        // Log.Debug(yaml);
        // var str = SaveUtility.SaveToString(CharacterDataList[0]);
        // var char2 = SaveUtility.LoadFromString<CharacterData>(str);
        // var yamlStr = YamlUtility.SaveToString(CharacterDataList[0]);
        // var char22=YamlUtility.LoadFromString<CharacterData>(yamlStr);
        // _userCharacterData = new CharacterData
        // {
        //     CharacterName = "桃子",
        //     Description = "桃子是一只可爱的黑猫，喜欢甜食和在树荫下晒太阳，希望有人可以与Ta聊天，如果被拒绝了会很悲伤。",
        // };
    }

    /// <summary>
    /// 获取角色，如果不存在，则返回默认角色
    /// </summary>
    /// <param name="characterName"></param>
    /// <returns></returns>
    public CharacterData GetCharacterData(string characterName)
    {
        if (CharacterDataDictionary.TryGetValue(characterName, out var characterData)) return characterData;
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Assistant);
    }

    /// <summary>
    /// 添加新角色，如果失败，则返回false
    /// </summary>
    /// <param name="characterData"></param>
    /// <returns></returns>
    public bool TryAddNewCharacterData(CharacterData characterData)
    {
        if (CharacterDataDictionary.TryAdd(characterData.CharacterName, characterData))
        {
            SaveCharacterData(characterData);
            OnCharacterAdded?.Invoke(characterData);
            return true;
        }

        return false;
    }

    public void DeleteCharacterData(CharacterData characterData)
    {
        DeleteCharacterData(characterData.CharacterName);
    }

    public void DeleteCharacterData(string characterName)
    {
        if (CharacterDataDictionary.ContainsKey(characterName))
        {
            var characterData = CharacterDataDictionary[characterName];
            CharacterDataDictionary.Remove(characterName);
            SaveUtility.Delete(Path.Combine(SettingConfig.SaveCharacterDataPath,
                characterData.CharacterName + ".json"));
            OnCharacterRemoved?.Invoke(characterData);
        }
    }

    public void SaveCharacterData(CharacterData characterData)
    {
        string path = SettingConfig.SaveCharacterDataPath;
        if (Enum.TryParse(characterData.CharacterName, out DefaultCharacter defaultCharacter))
        {
            //默认角色单独存储
            path = SettingConfig.SaveDefaultCharacterDataPath;
        }

        var savePath = Path.Combine(path, characterData.CharacterName + ".json");
        SaveUtility.Save(savePath, characterData);
        if (!characterData.IsHide) CharacterDataDictionary.TryAdd(characterData.CharacterName, characterData);
    }
}