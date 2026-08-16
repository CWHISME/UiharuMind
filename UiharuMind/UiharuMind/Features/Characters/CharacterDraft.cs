using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色编辑表单绑的那份<b>草稿</b>：所有 setter 写的都是一份深拷贝，
/// 只有 <see cref="TryCommit"/> 才会碰到活实例。取消就是把草稿丢掉，什么也不用撤。
///
/// 为什么要有这个类型，而不是让列表项自己兼职：列表项挂的是活实例，一兼职就意味着
/// 「编辑到一半」当场漏进列表、漏进正在进行的会话，且「取消」根本无从谈起。
/// 见 ADR 0014。
///
/// 提交是<b>往活实例上盖</b>（<see cref="CharacterData.CopyFrom"/>）而不是换实例：
/// 会话会把角色引用缓存起来，换实例会让正在对话的那一方继续用着旧对象。
/// </summary>
public partial class CharacterDraft : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly CharacterData _draft;

    private CharacterData? _origin; //null 表示这是个还没入库的新角色
    private string _baseline; //建草稿那一刻的序列化快照，脏检查用
    private AgentToolViewData? _agentTools;

    /// <summary>
    /// 角色档位。这是角色的唯一身份轴，界面的分类、徽章、表单面孔全从它来。
    /// </summary>
    public ECharacterKind Kind
    {
        get => _draft.Kind;
        set
        {
            if (_draft.Kind == value) return;
            _draft.Kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRoleplay));
            OnPropertyChanged(nameof(IsAgent));
            OnPropertyChanged(nameof(KindName));
            OnPropertyChanged(nameof(KindColor));
        }
    }

    /// <summary>可选档位(用户卡不可建，见 <see cref="CharacterKindPresentation.CreatableKinds"/>)</summary>
    public ECharacterKind[] SelectableKinds => CharacterKindPresentation.CreatableKinds;

    /// <summary>是否为角色扮演档(带开场白与用户卡的那一档)</summary>
    public bool IsRoleplay => Kind == ECharacterKind.Roleplay;

    /// <summary>是否为智能体档(装配工具与工作目录)</summary>
    public bool IsAgent => Kind.IsAgent();

    /// <summary>档位显示名(顶栏徽章)</summary>
    public string KindName => CharacterKindPresentation.NameOf(Kind);

    /// <summary>档位徽章底色</summary>
    public IImmutableSolidColorBrush KindColor => CharacterKindPresentation.ColorOf(Kind);

    /// <summary>内置角色不许改档位</summary>
    public bool IsDefault => _draft.IsDefaultCharacter;

    /// <summary>是否为尚未入库的新角色（顶栏据此把「保存」写成「创建」）</summary>
    public bool IsNew => _origin == null;

    /// <summary>
    /// 本草稿改的是哪个角色实例。已入库的是活实例，新建的是那个还没入库的种子——
    /// 两种情形下它都与列表项 <see cref="CharacterInfoViewData.Data"/> 是同一个对象，
    /// 「选中的还是不是我正在改的这个」因此一次引用比较就问得出来。
    /// </summary>
    public CharacterData Subject => _origin ?? _draft;

    /// <summary>草稿与建它时相比有没有改动</summary>
    public bool IsDirty => Snapshot() != _baseline;

    /// <summary>
    /// 注入用户卡：活引用，改了用户卡所有开着的角色跟着变
    /// </summary>
    public bool InjectUserCard
    {
        get => _draft.InjectUserCard;
        set
        {
            _draft.InjectUserCard = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 智能体的能力面板(工具开关 + 技能勾选)。惰性建:非智能体档的编辑页不显示这块,
    /// 建它要读盘解析技能包。它直写<b>草稿</b>身上那份能力配置,因此同样受取消保护
    /// </summary>
    public AgentToolViewData AgentTools => _agentTools ??= new AgentToolViewData(_draft.Tools, CapabilitySnapshot);

    /// <summary>
    /// 当前会话实际挂上的能力快照，用来给能力面板标估算占用。
    /// 从会话右栏点「编辑」进来时由调用方填上；从角色库进来时为 null——
    /// 那时占用显示「—」，因为没有运行期上下文，「关掉能省多少」确实答不出。
    /// <b>必须在首次访问 <see cref="AgentTools"/> 之前设好</b>（那一份是惰性建且只建一次）。
    /// </summary>
    public AgentCapabilitySnapshot? CapabilitySnapshot { get; set; }

    /// <summary>可委派的子智能体(存的是标识,界面显示解析后的名字)</summary>
    public ObservableCollection<MountedAgentItem> SubAgents { get; }

    /// <summary>已挂的子智能体标识与自己(选择器据此排除,自己是为了防递归)</summary>
    public IEnumerable<string> SubAgentAndSelfIds => SubAgents.Select(x => x.Id).Append(_draft.CharacterId);

    public string Name
    {
        get => _draft.CharacterName;
        set
        {
            _draft.CharacterName = value;
            OnPropertyChanged();
        }
    }

    public Bitmap? Icon
    {
        get => IconUtils.GetCharacterBitmapOrDefault(_draft);
        set
        {
            _draft.CharacterIcon = value.BitmapToBase64();
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => _draft.Description;
        set
        {
            _draft.Description = value;
            OnPropertyChanged();
        }
    }

    public string Template
    {
        get => _draft.Template;
        set
        {
            _draft.Template = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TemplateReadonly));
        }
    }

    public string FirstGreeting
    {
        get => _draft.FirstGreeting;
        set
        {
            _draft.FirstGreeting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstGreetingReadonly));
        }
    }

    /// <summary>参数替换后的提示词（{{$char}} 之类都已填好）</summary>
    public string TemplateReadonly =>
        string.IsNullOrEmpty(Template) ? "无" : _draft.TryRender(Template);

    /// <summary>参数替换后的开场白</summary>
    public string FirstGreetingReadonly =>
        string.IsNullOrEmpty(FirstGreeting) ? "无" : _draft.TryRender(FirstGreeting);

    public ChatPromptExecutionSettings ChatPromptExecutionSettings
    {
        get => _draft.Config.ExecutionSettings;
        set
        {
            _draft.Config.ExecutionSettings = value;
            OnPropertyChanged();
        }
    }

    private CharacterDraft(CharacterData? origin, CharacterData draft, IMessageService messageService)
    {
        _messageService = messageService;
        _origin = origin;
        _draft = draft;
        _baseline = Snapshot();

        SubAgents = new ObservableCollection<MountedAgentItem>(
            _draft.MountAgents.Select(MountedAgentItem.FromId));
        SubAgents.CollectionChanged += (_, _) =>
        {
            _draft.MountAgents = SubAgents.Select(x => x.Id).ToList();
        };
    }

    /// <summary>
    /// 为一个已有角色开一份草稿
    /// </summary>
    /// <param name="origin">角色库里那个活实例；提交时往它身上盖</param>
    /// <returns>草稿</returns>
    public static CharacterDraft ForEdit(CharacterData origin) =>
        new(origin, origin.DeepCopy(), App.Services.GetRequiredService<IMessageService>());

    /// <summary>
    /// 为一个还没入库的新角色开一份草稿
    /// </summary>
    /// <param name="seed">已定好档位（必要时预填提示词）的空角色</param>
    /// <returns>草稿</returns>
    public static CharacterDraft ForNew(CharacterData seed) =>
        new(null, seed, App.Services.GetRequiredService<IMessageService>());

    /// <summary>
    /// 提交草稿：校验名字、补正参数写法，然后写回活实例并落盘（新角色则入库）。
    /// </summary>
    /// <returns>提交成功返回 True；名字为空或入库失败返回 False，此时不该关掉编辑界面</returns>
    public bool TryCommit()
    {
        if (!CheckCharacterNameValid()) return false;

        _draft.NormalizeParams();
        if (_origin == null)
        {
            if (!CharacterManager.Instance.TryAddNewCharacterData(_draft))
            {
                _ = _messageService.ShowErrorAsync(Lang.AddDuplicateCharacterTips);
                return false;
            }

            _origin = _draft; //已入库,此后再提交走「盖回活实例」那条路
            _messageService.ShowNotification(Lang.AddCharacterSuccessTips, severity: MessageSeverity.Success);
        }
        else
        {
            _origin.CopyFrom(_draft);
            _origin.Save();
            CharacterManager.Instance.NotifyCharacterUpdated(_origin);
        }

        _baseline = Snapshot(); //刚提交完,草稿即干净
        OnPropertyChanged(nameof(IsNew));
        return true;
    }

    /// <summary>
    /// 名字非空校验；不合格时当场弹错
    /// </summary>
    /// <returns>合格返回 True</returns>
    public bool CheckCharacterNameValid()
    {
        if (string.IsNullOrEmpty(_draft.CharacterName))
        {
            _ = _messageService.ShowErrorAsync(Lang.CharacterEmptyNameTips);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 把一个智能体挂进可委派名单
    /// </summary>
    /// <param name="character">要挂的角色;自己、非智能体档、已挂的一律忽略</param>
    public void AddSubAgent(CharacterData character)
    {
        if (character.CharacterId == _draft.CharacterId) return; //防递归
        if (!character.Kind.IsAgent()) return;
        if (SubAgents.Any(x => x.Id == character.CharacterId)) return;

        SubAgents.Add(MountedAgentItem.FromId(character.CharacterId));
    }

    /// <summary>把一个子智能体移出名单</summary>
    /// <param name="item">名单项</param>
    [RelayCommand]
    public void RemoveSubAgent(MountedAgentItem item)
    {
        SubAgents.Remove(item);
    }

    /// <summary>
    /// 把一段片段插到提示词开头。<b>插进来之后就是自己的文本</b>，可随意改——
    /// 旧做法是运行期挂载别的角色，模型收到的那段话在编辑页里一个字也看不见。
    /// </summary>
    /// <param name="text">片段正文</param>
    public void InsertSnippet(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Template = string.IsNullOrWhiteSpace(Template) ? text : text + "\n\n" + Template;
    }

    /// <summary>
    /// 脏检查取的是整份序列化结果，而不是逐字段比对：字段是会加的，
    /// 漏比一个就意味着某类改动会被静默丢弃。只在切换/关闭这种低频时刻算一次。
    /// </summary>
    private string Snapshot() => SaveUtility.SaveToString(_draft);
}

/// <summary>
/// 子智能体名单项的显示包装：数据里存的是角色标识，界面上要显示可读的名字
/// </summary>
public sealed class MountedAgentItem
{
    /// <summary>被挂角色的标识</summary>
    public string Id { get; }

    /// <summary>显示名；角色不存在时回退为标识本身</summary>
    public string Name { get; }

    private MountedAgentItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>
    /// 由角色标识解析出显示名
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <returns>名单项</returns>
    public static MountedAgentItem FromId(string characterId)
    {
        string name = CharacterManager.Instance.GetCharacterData(characterId).CharacterName;
        return new MountedAgentItem(characterId, string.IsNullOrEmpty(name) ? characterId : name);
    }
}
