using System.Reflection;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Character;

public class CharacterManager : Singleton<CharacterManager>, IInitialize
{
    /// <summary>
    /// 已装载的角色，键为 <see cref="CharacterData.CharacterId"/>
    /// </summary>
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
        // 幂等:重复初始化不该抛"键已存在"
        CharacterDataDictionary.Clear();

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

                    // 文件名即 CharacterId;旧存档没有该字段时用文件名补上,避免每次加载生成新 Id
                    if (string.IsNullOrEmpty(characterData.CharacterId))
                        characterData.CharacterId = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(characterData.CharacterName))
                        characterData.CharacterName = characterData.CharacterId;
                    CharacterDataDictionary[characterData.CharacterId] = characterData;
                }
            }
        }

        //装载默认角色
        foreach (var defCharacter in DefaultCharacterManager.Instance.Characters)
        {
            if (CharacterDataDictionary.ContainsKey(defCharacter.Value.CharacterId)) continue;
            // 用户卡与 Empty 哨兵不进角色库:前者是"我是谁"的单例(有专属编辑窗),
            // 后者是"没有角色"的占位。两者都仍能经 GetCharacterData 按内置标识取到。
            if (defCharacter.Value.Kind == ECharacterKind.UserCard) continue;
            if (defCharacter.Key == DefaultCharacter.Empty) continue;
            CharacterDataDictionary.Add(defCharacter.Value.CharacterId, defCharacter.Value);
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
    /// 按标识获取角色，不存在则返回空角色
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <returns>角色数据</returns>
    public CharacterData GetCharacterData(string characterId)
    {
        if (CharacterDataDictionary.TryGetValue(characterId, out var characterData)) return characterData;

        // 始终隐藏的内置角色(Empty / UserCard)不进字典,只能从这里取。
        // 判据是 CharacterId 而非显示名,因此用户角色(GUID)不可能撞上内置角色。
        if (Enum.TryParse(characterId, out DefaultCharacter defaultCharacter))
        {
            return DefaultCharacterManager.Instance.GetCharacterData(defaultCharacter);
        }

        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Empty);
    }

    /// <summary>
    /// 添加新角色，标识重复则返回 false
    /// </summary>
    /// <param name="characterData">角色数据</param>
    /// <returns>是否添加成功</returns>
    public bool TryAddNewCharacterData(CharacterData characterData)
    {
        if (CharacterDataDictionary.TryAdd(characterData.CharacterId, characterData))
        {
            SaveCharacterData(characterData);
            OnCharacterAdded?.Invoke(characterData);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 删除角色及其存档
    /// </summary>
    /// <param name="characterData">角色数据</param>
    public void DeleteCharacterData(CharacterData characterData)
    {
        DeleteCharacterData(characterData.CharacterId);
    }

    /// <summary>
    /// 删除角色及其存档
    /// </summary>
    /// <param name="characterId">角色标识</param>
    public void DeleteCharacterData(string characterId)
    {
        if (!CharacterDataDictionary.Remove(characterId, out CharacterData? characterData)) return;
        SaveUtility.Delete(GetSavePath(characterData));
        OnCharacterRemoved?.Invoke(characterData);
    }

    /// <summary>
    /// 保存角色存档
    /// </summary>
    /// <param name="characterData">角色数据</param>
    public void SaveCharacterData(CharacterData characterData)
    {
        SaveUtility.Save(GetSavePath(characterData), characterData);
        if (characterData.Kind != ECharacterKind.UserCard)
            CharacterDataDictionary.TryAdd(characterData.CharacterId, characterData);
    }

    /// <summary>
    /// 存档路径。文件名即 CharacterId，因此角色改名不动文件，
    /// 显示名里的非法字符也不再可能造成路径穿越。
    /// </summary>
    private static string GetSavePath(CharacterData characterData)
    {
        // 内置角色的覆盖文件单独放一个目录:清空该目录 = 全部恢复出厂
        string path = Enum.TryParse(characterData.CharacterId, out DefaultCharacter _)
            ? SettingConfig.SaveDefaultCharacterDataPath
            : SettingConfig.SaveCharacterDataPath;
        return Path.Combine(path, characterData.CharacterId + ".json");
    }
}