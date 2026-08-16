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
    /// 角色种类
    /// </summary>
    public ECharacterKind Kind { get; set; } = ECharacterKind.Roleplay;

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
    /// 名单里每一项是一个智能体档角色，<c>RunSubAgent</c> 据此让模型挑一个派活；
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
        Kind = snapshot.Kind;
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
