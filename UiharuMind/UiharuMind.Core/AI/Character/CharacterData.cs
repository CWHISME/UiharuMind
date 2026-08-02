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
    /// 显示名(<see cref="CharacterName"/>)可随意改动而不断开会话与挂载引用。
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
    /// 始终在角色列表隐藏
    /// </summary>
    public bool IsHide { get; set; }

    /// <summary>
    /// 是否默认隐藏
    /// </summary>
    public bool IsHideDefault { get; set; }

    /// <summary>
    /// 是否要求视觉模型（识图类角色）
    /// </summary>
    public bool RequiresVisionModel { get; set; }

    /// <summary>
    /// 内联挂载：按顺序取这些角色的 Template，render 后拼进本角色的系统提示。
    /// 挂载 <see cref="DefaultCharacter.Roleplay_ThirdPerson"/> 之类即"带角色扮演脚手架"，
    /// 挂载 <see cref="DefaultCharacter.UserCard"/> 即"注入用户人格"。
    /// 默认为空——纯提示词角色(翻译、识图等)不该被塞进扮演模板。
    /// </summary>
    public List<string> MountPrompts { get; set; } = [];

    /// <summary>
    /// 委托挂载：把这些角色注册为可被本 agent 自主调度的子 agent，
    /// 由子 agent 的 Description 作为能力广告。仅 <see cref="ECharacterKind.Agent"/> 生效。
    /// </summary>
    public List<string> MountAgents { get; set; } = [];

    /// <summary>
    /// 是否为纯提示词角色：未挂载任何提示词片段，因而不带角色扮演脚手架、不注入用户卡。
    /// 取代原先的 IsTool 旗标——"工具人"与"扮演角色"的区别现在由挂载列表决定，
    /// 界面的分类、图标与筛选据此派生。
    /// </summary>
    [JsonIgnore]
    public bool IsPurePromptCharacter => MountPrompts.Count == 0;

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
    /// 单纯的描述。被 <see cref="MountAgents"/> 委托挂载时，作为该子 agent 的能力广告，
    /// 主 agent 据此决定何时委托。
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
    /// 对话模板，主要用于角色扮演，可选，会作为系统回复的基础
    /// </summary>
    public string DialogTemplate { get; set; } = "";

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
}
