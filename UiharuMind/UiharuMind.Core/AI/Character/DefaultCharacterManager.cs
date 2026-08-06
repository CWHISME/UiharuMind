using System.Reflection;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character;

public class DefaultCharacterManager : Singleton<DefaultCharacterManager>, IInitialize
{
    /// <summary>
    /// 已装载的内置角色。整体替换而非就地清空重填——
    /// 后者会让重建期间的读取方看到半填充状态(读到"键不存在"),
    /// 只给写入方加锁是挡不住这个的。
    /// </summary>
    public IReadOnlyDictionary<DefaultCharacter, CharacterData> Characters { get; private set; } =
        new Dictionary<DefaultCharacter, CharacterData>();

    public void OnInitialize()
    {
        // 幂等:重复初始化(如重载内置角色)不该抛"键已存在"
        Characters = LoadDefaultCharacters();
    }

    private static Dictionary<DefaultCharacter, CharacterData> LoadDefaultCharacters()
    {
        Dictionary<DefaultCharacter, CharacterData> loaded = new();

        const int max = (int)DefaultCharacter.Max;
        for (int i = 0; i < max; i++)
        {
            var character = (DefaultCharacter)i;
            // 枚举名即内置角色的 CharacterId,也是内置资源与覆盖文件的文件名
            var characterId = character.ToString();

            string fileName = characterId + ".json";
            string externalFileName = Path.Combine(SettingConfig.SaveDefaultCharacterDataPath, fileName);
            CharacterData characterData = File.Exists(externalFileName)
                ? SaveUtility.Load<CharacterData>(externalFileName) ??
                  EmbeddedResourcesUtils.ReadFromJson<CharacterData>(fileName)
                : EmbeddedResourcesUtils.ReadFromJson<CharacterData>(fileName);

            characterData.IsDefaultCharacter = true;
            characterData.CharacterId = characterId;
            loaded[character] = characterData;
        }

        return loaded;
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

    // 角色扮演脚手架(第一/第三人称)已不是角色:它们只是可插入提示词框的文本,
    // 现由 PromptSnippetManager 管，见 ADR 0003
    UiharuKazari,

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
    /// 语法分析
    /// </summary>
    AssistantSyntacticAnalysis,

    /// <summary>
    /// 思维链
    /// </summary>
    ChainofThought,
    // /// <summary>
    // /// 文本思考：
    // /// </summary>
    // TextThinker,
    
    /// <summary>
    /// 智能体(Kind = Agent)
    /// </summary>
    WorkspaceAgent,

    Translator,
    TranslatorAdvanced,
    VisionOcr,
    Vision,
    Max
}