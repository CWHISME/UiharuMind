using System.Reflection;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 内置角色的<b>身份</b>枚举：只含代码会按名字点名的角色。
///
/// 内容卡(白猫、晨曦、白露、魔禁班底等)不占枚举位——它们只是 <c>Resources/Cards/</c> 下的一张卡，
/// 由 <see cref="DefaultCharacterManager"/> 扫描目录装载，CharacterId = 文件名。
/// 枚举名即 CharacterId、资源文件名与覆盖文件名，三者必须一致(有不变量测试钉住)。
/// 改名只在加了 <see cref="BuiltInCharacterId"/> 旧名映射时允许，否则老会话存档会断。
/// </summary>
public enum DefaultCharacter
{
    /// <summary>空角色，找不到角色时的默认哨兵</summary>
    None,

    /// <summary>用户角色卡(「我是谁」的单例)</summary>
    UserCard,

    /// <summary>默认智能体(晨曦):新建智能体会话与定时任务会话默认用它</summary>
    ChenXiAgent,

    /// <summary>匿名委派会话的身份载体(内部角色，提示词现拼)</summary>
    AnonymousAgent,

    /// <summary>老的只读子会话身份载体(内部角色，仅为重建存量)</summary>
    LegacyExploreAgent,

    /// <summary>翻译</summary>
    TranslationPrompt,

    /// <summary>高级翻译</summary>
    AdvancedTranslationPrompt,

    /// <summary>识图理解</summary>
    ImageVisionPrompt,

    /// <summary>识图 OCR</summary>
    ImageOcrPrompt,

    /// <summary>解释内容</summary>
    ExplainPrompt,

    /// <summary>高级专家</summary>
    ExpertPrompt,

    /// <summary>高级专家(带额外引用信息)</summary>
    ExpertQuotePrompt,

    /// <summary>语法分析</summary>
    SyntacticAnalysisPrompt,

    /// <summary>思维链</summary>
    ChainOfThoughtPrompt,

    Max
}

/// <summary>
/// 内置角色改名的<b>旧名 → 现行名</b>别名表。老会话存档、老覆盖文件里的旧 CharacterId 靠它归一到现行名。
///
/// 只读不写：它存在的唯一理由是迁移，不参与任何新数据的落盘。
/// <b>UiharuKazari(旧角色扮演卡)刻意不在此列</b>——它退役不迁移，老会话落到哨兵。
/// </summary>
public static class BuiltInCharacterId
{
    private static readonly IReadOnlyDictionary<string, string> LegacyToCanonical = new Dictionary<string, string>
    {
        ["Empty"] = nameof(DefaultCharacter.None),
        ["WorkspaceAgent"] = "UiharuKazariAgent",
        ["GeneralSubAgent"] = nameof(DefaultCharacter.AnonymousAgent),
        ["ExploreSubAgent"] = nameof(DefaultCharacter.LegacyExploreAgent),
        ["Translator"] = nameof(DefaultCharacter.TranslationPrompt),
        ["TranslatorAdvanced"] = nameof(DefaultCharacter.AdvancedTranslationPrompt),
        ["Vision"] = nameof(DefaultCharacter.ImageVisionPrompt),
        ["VisionOcr"] = nameof(DefaultCharacter.ImageOcrPrompt),
        ["AssistantExplain"] = nameof(DefaultCharacter.ExplainPrompt),
        ["AssistantExpert"] = nameof(DefaultCharacter.ExpertPrompt),
        ["AssistantExpertQuote"] = nameof(DefaultCharacter.ExpertQuotePrompt),
        ["AssistantSyntacticAnalysis"] = nameof(DefaultCharacter.SyntacticAnalysisPrompt),
        ["ChainofThought"] = nameof(DefaultCharacter.ChainOfThoughtPrompt),
    };

    /// <summary>把任意 CharacterId 归一到现行名；非旧名原样返回</summary>
    public static string Resolve(string characterId) =>
        LegacyToCanonical.TryGetValue(characterId, out string? canonical) ? canonical : characterId;

    /// <summary>现行名对应哪些旧名(覆盖文件兼容用)</summary>
    public static IEnumerable<string> LegacyNamesOf(string canonicalId) =>
        LegacyToCanonical.Where(kv => kv.Value == canonicalId).Select(kv => kv.Key);
}

public class DefaultCharacterManager : Singleton<DefaultCharacterManager>, IInitialize
{
    /// <summary>
    /// 已装载的全部内置卡，键为 CharacterId(枚举名或内容卡文件名)。
    /// 整体替换而非就地清空重填——后者会让重建期间的读取方看到半填充状态。
    /// </summary>
    public IReadOnlyDictionary<string, CharacterData> All { get; private set; } =
        new Dictionary<string, CharacterData>();

    public void OnInitialize()
    {
        // 幂等:重复初始化(如重载内置角色)不该抛"键已存在"
        string[] resourceNames = typeof(DefaultCharacterManager).Assembly.GetManifestResourceNames();
        All = LoadAll(resourceNames);
    }

    /// <summary>按身份枚举取内置卡。枚举名即 CharacterId</summary>
    public CharacterData GetCharacterData(DefaultCharacter character) => All[character.ToString()];

    private static Dictionary<string, CharacterData> LoadAll(string[] resourceNames)
    {
        string assemblyPrefix = $"{typeof(DefaultCharacterManager).Assembly.GetName().Name}.Resources.";
        // id → 资源基名(相对 Resources，如 "Cards.Agents.ChenXiAgent")
        Dictionary<string, string> cardBases = EnumerateCardBases(resourceNames, assemblyPrefix);

        // 枚举成员必须有卡:改枚举名漏了卡文件要当场炸，不留到运行期
        foreach (DefaultCharacter value in Enum.GetValues<DefaultCharacter>())
        {
            if (value == DefaultCharacter.Max) continue;
            if (!cardBases.ContainsKey(value.ToString()))
                throw new FileNotFoundException($"内置卡缺失:{value} 在 Resources/Cards 下找不到对应的 .json");
        }

        Dictionary<string, CharacterData> loaded = new();
        foreach (var (id, baseName) in cardBases)
        {
            loaded[id] = LoadCard(id, baseName, resourceNames, assemblyPrefix);
        }

        return loaded;
    }

    /// <summary>
    /// 扫描 <c>Resources/Cards/</c> 下(含 Agents / Tools / Characters 子目录)的 *.json，
    /// 产出 <c>id → 资源基名</c>。目录只是分类提示，身份以卡上的 IsAgent / IsInternal 为准。
    /// </summary>
    private static Dictionary<string, string> EnumerateCardBases(string[] resourceNames, string assemblyPrefix)
    {
        const string cardsPrefix = "Cards.";
        const string suffix = ".json";
        Dictionary<string, string> bases = new();
        foreach (string name in resourceNames)
        {
            if (!name.StartsWith(assemblyPrefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            string baseName = name.Substring(assemblyPrefix.Length, name.Length - assemblyPrefix.Length - suffix.Length);
            if (!baseName.StartsWith(cardsPrefix, StringComparison.Ordinal)) continue;

            // CharacterId = 文件名；点只作目录与资源名的分隔。文件名带点会在此截断丢名，
            // 静默装错比当场报错更糟——显式拒绝并提示改名
            int subDirEnd = baseName.IndexOf('.', cardsPrefix.Length);
            string fileName = subDirEnd < 0 ? baseName[cardsPrefix.Length..] : baseName[(subDirEnd + 1)..];
            if (fileName.Length == 0)
                throw new InvalidOperationException($"内置卡资源名不合法(缺文件名段): {name}");
            if (fileName.Contains('.'))
                throw new InvalidOperationException(
                    $"内置卡文件名不得含点(CharacterId=文件名): {fileName}，请改名后再装");

            bases[fileName] = baseName;
        }

        return bases;
    }

    private static CharacterData LoadCard(string id, string baseName, string[] resourceNames, string assemblyPrefix)
    {
        // 覆盖文件优先(现行名 → 旧名)——它自带内联的人格，不需要再读 .md
        CharacterData data = LoadOverride(id) ?? LoadEmbedded(baseName, resourceNames, assemblyPrefix);

        data.IsDefaultCharacter = true;
        data.CharacterId = id;
        return data;
    }

    /// <summary>嵌入卡 = 元数据(<c>&lt;base&gt;.json</c>) + 人格正文(<c>&lt;base&gt;.md</c>，可缺省)</summary>
    private static CharacterData LoadEmbedded(string baseName, string[] resourceNames, string assemblyPrefix)
    {
        CharacterData data = EmbeddedResourcesUtils.ReadFromJson<CharacterData>($"{baseName}.json");

        if (Array.IndexOf(resourceNames, $"{assemblyPrefix}{baseName}.md") >= 0)
            data.Template = EmbeddedResourcesUtils.Read($"{baseName}.md");

        return data;
    }

    private static CharacterData? LoadOverride(string id)
    {
        foreach (string name in new[] { id }.Concat(BuiltInCharacterId.LegacyNamesOf(id)))
        {
            string path = Path.Combine(AppPaths.Data.CharacterOverrides, name + ".json");
            if (File.Exists(path) && SaveUtility.Load<CharacterData>(path) is { } data)
                return data;
        }

        return null;
    }
}
