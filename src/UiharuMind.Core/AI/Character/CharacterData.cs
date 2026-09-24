using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
    /// <b>这是角色身份的唯一轴</b>（ADR 0043）。从前是四档枚举，
    /// 但那四档里只有一条真的机械分界线——<b>开不开 harness</b>。
    /// 扮演与工具人之间没有任何装配差异（两档都走 <c>AgentOptionsFactory.BuildPromptOnlyOptions</c>），
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
    /// 老存档里的 <c>"Kind"</c> 字段——<b>只读进来，不写出去</b>。
    ///
    /// 它存在的唯一理由是迁移：升级前的角色卡身上是 <c>"Kind": "Agent"|"Roleplay"|"Tool"|"UserCard"</c>，
    /// 没有这个 setter，反序列化会让它们全部落到 <see cref="IsAgent"/> 的默认值 false ——
    /// <b>现存的智能体会静默降级成普通角色</b>。映射进新轴之后就不用任何人手动改卡。
    /// 只有 setter，所以它不参与序列化：新卡写出去的只有 IsAgent / IsUserCard。
    /// 收 <see cref="JsonElement"/> 而不是字符串：读到意外的形态只当普通角色，不让整张卡读不进来。
    /// </summary>
    [JsonPropertyName("Kind")]
    public JsonElement LegacyKind
    {
        set
        {
            if (value.ValueKind != JsonValueKind.String) return;
            string? kind = value.GetString();
            IsAgent = kind == "Agent";
            IsUserCard = kind == "UserCard";
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
    /// 屏蔽角色：随程序内置、但<b>默认不出现在角色库与任何选择器里</b>，
    /// 只有开发者用官方网址解锁后才可见（见 <see cref="CharacterVisibility"/>）。
    ///
    /// 与 <see cref="IsInternal"/> 的分工：内部角色是「程序点名取用、给用户看只是为了改提示词」，
    /// 开关是角色库右上角那个「显示内部角色」；屏蔽角色是「内容本身先不给你看」，
    /// 开关是开发者手段，刻意不给普通入口。
    /// </summary>
    public bool IsShielded { get; set; }

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
    /// 本智能体的能力配置(装哪些工具、禁用哪些技能)。只对智能体(<see cref="IsAgent"/>)有意义；
    /// 运行时只读这一份，没有全局总闸(见 ADR 0003)。翻回普通角色时刻意不清，翻回来不丢配置。
    /// </summary>
    public AgentToolConfig Tools { get; set; } = new();

    /// <summary>
    /// 可委派的子智能体名单(只对智能体有意义)：
    /// 名单里每一项是一个智能体，<c>SendMessage</c> 的收件人据此点名；
    /// 为空则退回内置的通用匿名子代理。
    /// 装配时按身份过滤而非信任存档——名单里的角色可能已经翻回普通角色。
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
    /// 手写人格锚点：<see cref="GetPersonaCoda"/> 的显式写法，为空则回退到名 + 描述自动拼。
    /// 覆盖文件与内置卡 JSON 经 <c>Config.PromptConfig.anchor</c> 入库。
    /// </summary>
    [JsonIgnore]
    public string PersonaAnchor
    {
        get => Config.PromptConfig.Anchor ?? "";
        set => Config.PromptConfig.Anchor = value;
    }

    /// <summary>
    /// 人格 coda：系统提示末尾的一句身份回锚（静态版重锚，见提案 v8 §7.4）。
    ///
    /// 人格在提示词开头，工具结果越堆越长时会被淹没（实机见过 43k tokens 的轮次）；
    /// 模型对开头和结尾最敏感，结尾钉一句身份即吃到 recency 权重。
    /// 它是静态前缀的一部分，不打碎前缀缓存——这是它相对动态重锚的唯一优势，
    /// 动态版（每轮贴）仍是治历史漂移的正解，见提案。
    ///
    /// <b>fallback 链</b>：手写锚点（<see cref="PersonaAnchor"/>，原样返回，写卡的人全权控制措辞）
    /// → 名 + 描述自动拼（描述本就是定位语）→ 无名则为空串（不发）。
    /// 自动拼的那句只有身份没有风格，长循环里钉不住语气——有人格稿的角色应在卡上配手写锚点
    /// （提案 v8 §7.4 的重锚句改写成第二人称）。
    ///
    /// 措辞用第二人称（<c>你是…</c>）：系统提示全篇都是对模型说话的"你"，
    /// 结尾换第三人称标签等于换了个声音；"你是"才是训练里标准的角色指派句式。
    /// </summary>
    /// <returns>回锚句；无名且无手写锚点时为空串（不发）</returns>
    public string GetPersonaCoda()
    {
        if (!string.IsNullOrWhiteSpace(PersonaAnchor)) return PersonaAnchor.Trim();
        if (string.IsNullOrWhiteSpace(CharacterName)) return string.Empty;
        if (string.IsNullOrWhiteSpace(Description)) return $"你是{CharacterName}。";
        return $"你是{CharacterName}，{Description}";
    }

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
        IsShielded = snapshot.IsShielded;
        InjectUserCard = snapshot.InjectUserCard;
        RequiresVisionModel = snapshot.RequiresVisionModel;
        Tools = snapshot.Tools;
        MountAgents = snapshot.MountAgents;
        CharacterIcon = snapshot.CharacterIcon;
        FirstGreeting = snapshot.FirstGreeting;
        _memory = null; //记忆库名可能变了，缓存作废
    }
}
