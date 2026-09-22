using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character;

public class CharacterData
{
    private MemoryData? _memory;

    public CharacterConfig Config { get; set; } = new CharacterConfig();

    /// <summary>
    /// 角色的稳定标识，同时是存档文件名。
    /// 内置角色为 <see cref="DefaultCharacter"/> 的枚举名，用户角色为 GUID。
    /// 显示名(<see cref="CharacterName"/>)可随意改动而不断开会话与名单引用。
    /// </summary>
    public string CharacterId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 走不走 agent 装配：工具、工作目录、权限档与框架 harness。
    ///
    /// <b>这是角色身份的唯一存储轴</b>（ADR 0043）。从前是四档枚举 <c>ECharacterKind</c>，
    /// 但那四档里只有一条真的机械分界线——<b>开不开 harness</b>。
    /// 扮演与工具人之间没有任何装配差异（两档都走 <c>BuildRoleplayOptions</c>），
    /// 它们的区别是<b>这张卡上填了什么</b>（有没有人格与开场白），不是走哪条管线。
    /// </summary>
    public bool IsAgent { get; set; }

    /// <summary>
    /// 是不是「我是谁」那张用户卡。单例、有专属编辑窗、不进角色库、不能对话。
    ///
    /// 它<b>不在</b> <see cref="IsAgent"/> 那条轴上——不对话的东西谈不上开不开 harness，
    /// 所以单独一个标记而不是派生。方向是让它彻底退出角色体系（走自己的数据形态，
    /// 届时也能支持多张用户卡），本轮不做。
    /// </summary>
    public bool IsUserCard { get; set; }

    /// <summary>
    /// 角色档位的<b>派生视图</b>，只读。留着是为了让既有的
    /// <c>Kind.IsAgent()</c> / <c>Kind.IsChat()</c> / <c>Kind.CanStartSession()</c> 判定原样成立。
    ///
    /// ⚠️ <b>永远不会产出 <see cref="ECharacterKind.Tool"/></b>：扮演与工具人已经合并
    /// （ADR 0043），存量的工具人卡一律投影成 <see cref="ECharacterKind.Roleplay"/>。
    /// 这个投影本身是过渡件，ADR 0043 阶段 2 会连同枚举一起删掉。
    /// </summary>
    [JsonIgnore]
    public ECharacterKind Kind => IsUserCard
        ? ECharacterKind.UserCard
        : IsAgent
            ? ECharacterKind.Agent
            : ECharacterKind.Roleplay;

    /// <summary>
    /// 老存档里的 <c>"Kind"</c> 字段——<b>只读进来，不写出去</b>。
    ///
    /// 它存在的唯一理由是迁移：升级前的角色卡身上是 <c>"Kind": "Agent"|"Roleplay"|"Tool"|"UserCard"</c>，
    /// 没有这个 setter，反序列化会让它们全部落到 <see cref="IsAgent"/> 的默认值 false ——
    /// <b>现存的智能体会静默降级成普通角色</b>。映射进新轴之后就不用任何人手动改卡。
    /// 只有 setter，所以它不参与序列化：新卡写出去的只有 IsAgent / IsUserCard。
    /// </summary>
    [JsonPropertyName("Kind")]
    public ECharacterKind LegacyKind
    {
        set
        {
            IsAgent = value == ECharacterKind.Agent;
            IsUserCard = value == ECharacterKind.UserCard;
        }
    }

    /// <summary>
    /// 记忆库
    /// </summary>
    public string MemoryName { get; set; } = "";

    /// <summary>
    /// 是否是默认角色
    /// </summary>
    public bool IsDefaultCharacter { get; set; }

    /// <summary>
    /// 内部角色：程序按 <see cref="DefaultCharacter"/> 点名取用(识图、翻译、解释等技能)。
    /// 角色库默认不列，打开「显示内部角色」才可见并可编辑；
    /// 不进任何选择器的候选。<b>只表示可见性</b>，身份仍由 <see cref="Kind"/> 说。
    /// </summary>
    public bool IsInternal { get; set; }

    /// <summary>
    /// 注入用户卡：把 <see cref="DefaultCharacter.UserCard"/> 的模板拼进本角色的系统提示。
    /// 是<b>活引用</b>——改了用户卡，所有打开此开关的角色下一轮就跟着变。
    /// 这是运行期<b>唯一</b>一处跨角色引用；其余提示词组合都在编辑期完成(插入片段)。
    /// </summary>
    public bool InjectUserCard { get; set; }

    /// <summary>
    /// 是否要求视觉模型（识图类角色）
    /// </summary>
    public bool RequiresVisionModel { get; set; }

    /// <summary>
    /// 本智能体的能力配置(装哪些工具、禁用哪些技能)。只对 <see cref="ECharacterKind.Agent"/> 有意义；
    /// 运行时只读这一份，没有全局总闸(见 ADR 0003)。
    /// </summary>
    public AgentToolConfig Tools { get; set; } = new();

    /// <summary>
    /// 可委派的子智能体名单(只对 <see cref="ECharacterKind.Agent"/> 有意义)：
    /// 名单里每一项是一个智能体档角色，<c>RunAgent</c> 据此让模型挑一个派活；
    /// 为空则退回内置的通用匿名子代理。
    /// 装配时按档位过滤而非信任存档——旧存档里这里可能躺着工具人角色。
    /// </summary>
    public List<string> MountAgents { get; set; } = [];

    /// <summary>
    /// 角色名
    /// </summary>
    [JsonIgnore]
    public string CharacterName
    {
        get => Config.PromptConfig.Name ?? "";
        set => Config.PromptConfig.Name = value;
    }

    /// <summary>
    /// 单纯的描述
    /// </summary>
    [JsonIgnore]
    public string Description
    {
        get => Config.PromptConfig.Description ?? "";
        set => Config.PromptConfig.Description = value;
    }

    /// <summary>
    /// 角色的指令模板，会作为系统提示词的基础
    /// 参数由 {{$}} 构成，具体含义由具体的模板决定
    /// </summary>
    [JsonIgnore]
    public string Template
    {
        get => Config.PromptConfig.Template ?? "";
        set => Config.PromptConfig.Template = value;
    }

    /// <summary>
    /// 存储的文件日期
    /// </summary>
    [JsonIgnore]
    public long FileDateTime { get; set; }

    /// <summary>
    /// 记忆
    /// </summary>
    [JsonIgnore]
    public MemoryData? Memory =>
        _memory ??= MemoryManager.Instance.GetMemoryData(MemoryName);

    /// <summary>
    /// 角色头像，以 Base64 编码的图片数据
    /// </summary>
    public string CharacterIcon { get; set; } = "";

    /// <summary>
    /// 开场白，可选，会作为系统回复的开头
    /// </summary>
    public string FirstGreeting { get; set; } = "";

    /// <summary>
    /// 尝试将指定内容的占位内容替换为实际内容
    /// {{$char}} 代表角色名
    /// {{$user}} 代表用户名
    /// </summary>
    /// <param name="template"></param>
    public string TryRender(string template)
    {
        return CharacterPromptRenderer.Render(template, BuildPromptArguments(null));
    }

    /// <summary>
    /// 保存之前的参数的有效性检查并替换，避免填错参数导致的错误
    /// </summary>
    public string ParamsValidReplacer(string str)
    {
        return str.Replace("{{char}}", "{{$char}}").Replace("{{user}}", "{{$user}}");
    }

    /// <summary>
    /// 落盘前把两处自由文本里漏写 <c>$</c> 的参数补正。
    /// 编辑提交与角色卡导入都走这里，免得两条入库路径对参数写法的宽容度不一样。
    /// </summary>
    public void NormalizeParams()
    {
        Template = ParamsValidReplacer(Template);
        FirstGreeting = ParamsValidReplacer(FirstGreeting);
    }

    public void Save()
    {
        CharacterManager.Instance.SaveCharacterData(this);
    }

    public void Copy()
    {
        // 主键是 CharacterId,显示名允许重复,因此不再需要靠改名试探唯一性
        var newCharData = DeepCopy();
        newCharData.CharacterId = Guid.NewGuid().ToString("N");
        newCharData.IsDefaultCharacter = false;
        newCharData.CharacterName += "_Copy";
        CharacterManager.Instance.TryAddNewCharacterData(newCharData);
    }

    public void Delete()
    {
        CharacterManager.Instance.DeleteCharacterData(this);
    }

    // ============================== Common Params ================================

    public const string ParamsNameLanguage = "lang";
    public const string ParamsNameLanguageDefault = "lang_default";
    public const string ParamsNameChar = "char";
    public const string ParamsNameUser = "user";

    public Dictionary<string, object?> BuildPromptArguments(Dictionary<string, object?>? arguments)
    {
        arguments ??= new Dictionary<string, object?>();
        arguments.TryAdd(ParamsNameLanguage, LanguageUtils.CurCultureInfo.DisplayName);
        arguments.TryAdd(ParamsNameLanguageDefault, LanguageUtils.CurCultureInfo.DisplayName);
        arguments.TryAdd(ParamsNameChar, CharacterName);
        arguments.TryAdd(ParamsNameUser, CharacterManager.Instance.UserCharacterName);
        return arguments;
    }

    //================================================================================

    public CharacterData DeepCopy()
    {
        var tmpStr = SaveUtility.SaveToString(this);
        return (SaveUtility.LoadFromString<CharacterData>(tmpStr));
    }

    /// <summary>
    /// 把另一份角色的全部可持久化状态搬进本实例。
    ///
    /// <b>就地覆盖而不是换实例</b>：会话拿到角色后会把引用缓存起来
    /// （<c>ChatSession._characterData</c>），换实例会让正在进行的会话继续用着旧对象。
    /// 编辑页的「保存」因此走这里——草稿改完往活实例上一盖，谁持有它谁就跟着变。
    ///
    /// 逐字段赋值而非反射：字段少、读起来直白。<b>新增可持久化字段时这里要跟着加</b>，
    /// 漏了会在 <c>CharacterDataCopyFromTests</c> 里当场炸出来。
    /// </summary>
    /// <param name="other">来源角色；本方法只读它，不持有它的任何子对象</param>
    public void CopyFrom(CharacterData other)
    {
        CharacterData snapshot = other.DeepCopy(); //深拷一份再搬，免得两个实例共享 Config/Tools 等子对象
        Config = snapshot.Config;
        CharacterId = snapshot.CharacterId;
        IsAgent = snapshot.IsAgent;
        IsUserCard = snapshot.IsUserCard;
        MemoryName = snapshot.MemoryName;
        IsDefaultCharacter = snapshot.IsDefaultCharacter;
        IsInternal = snapshot.IsInternal;
        InjectUserCard = snapshot.InjectUserCard;
        RequiresVisionModel = snapshot.RequiresVisionModel;
        Tools = snapshot.Tools;
        MountAgents = snapshot.MountAgents;
        CharacterIcon = snapshot.CharacterIcon;
        FirstGreeting = snapshot.FirstGreeting;
        _memory = null; //记忆库名可能变了，缓存作废
    }
}
