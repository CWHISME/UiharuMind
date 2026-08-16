using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Shell;
using Ursa.Controls;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation;

namespace UiharuMind.Features.Characters;

public partial class CharacterInfoViewData : ObservableObject
{
    private readonly IMessageService _messageService;

    /// <summary>
    /// 角色标识（挂载引用与会话引用都用它，改名不会断链）
    /// </summary>
    public string CharacterId => _characterData.CharacterId;

    public bool IsDefault => _characterData.IsDefaultCharacter;

    /// <summary>
    /// 角色档位。这是角色的唯一身份轴，界面的分类、徽章、表单面孔全从它来。
    /// </summary>
    public ECharacterKind Kind
    {
        get => _characterData.Kind;
        set
        {
            if (_characterData.Kind == value) return;
            _characterData.Kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRoleplay));
            OnPropertyChanged(nameof(IsAgent));
            OnPropertyChanged(nameof(KindName));
            OnPropertyChanged(nameof(KindColor));
            OnPropertyChanged(nameof(Icon));
        }
    }

    /// <summary>可选档位(用户卡不可建，见 <see cref="CharacterKindPresentation.CreatableKinds"/>)</summary>
    public ECharacterKind[] SelectableKinds => CharacterKindPresentation.CreatableKinds;

    /// <summary>是否为角色扮演档(带开场白、示例对话与用户卡的那一档)</summary>
    public bool IsRoleplay => Kind == ECharacterKind.Roleplay;

    /// <summary>是否为智能体档(装配工具与工作目录)</summary>
    public bool IsAgent => Kind.IsAgent();

    /// <summary>
    /// 注入用户卡：活引用，改了用户卡所有开着的角色跟着变
    /// </summary>
    public bool InjectUserCard
    {
        get => _characterData.InjectUserCard;
        set
        {
            _characterData.InjectUserCard = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 智能体的能力面板(工具开关 + 技能勾选)。惰性建:非智能体档的编辑页不显示这块,
    /// 建它要读盘解析技能包
    /// </summary>
    public AgentToolViewData AgentTools => _agentTools ??= new AgentToolViewData(_characterData.Tools);

    /// <summary>可委派的子智能体(存的是标识,界面显示解析后的名字)</summary>
    public ObservableCollection<MountedAgentItem> SubAgents { get; }

    /// <summary>已挂的子智能体标识与自己(选择器据此排除,自己是为了防递归)</summary>
    public IEnumerable<string> SubAgentAndSelfIds => SubAgents.Select(x => x.Id).Append(CharacterId);

    /// <summary>档位显示名(列表徽章)</summary>
    public string KindName => CharacterKindPresentation.NameOf(Kind);

    /// <summary>档位徽章底色</summary>
    public IImmutableSolidColorBrush KindColor => CharacterKindPresentation.ColorOf(Kind);

    public long FileDateTime => _characterData.FileDateTime;

    public string Name
    {
        get => _characterData.CharacterName;
        set
        {
            _characterData.CharacterName = value;
            OnPropertyChanged();
        }
    }

    public Bitmap? Icon
    {
        get => IconUtils.GetCharacterBitmapOrDefault(_characterData);
        set
        {
            _characterData.CharacterIcon = value.BitmapToBase64();
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => _characterData.Description;
        set
        {
            _characterData.Description = value;
            OnPropertyChanged();
        }
    }

    public string Template
    {
        get => _characterData.Template;
        set
        {
            _characterData.Template = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TemplateReadonly));
        }
    }

    public string DialogTemplate
    {
        get => _characterData.DialogTemplate;
        set
        {
            _characterData.DialogTemplate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DialogTemplateReadonly));
        }
    }

    public string FirstGreeting
    {
        get => _characterData.FirstGreeting;
        set
        {
            _characterData.FirstGreeting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstGreetingReadonly));
        }
    }

    public string TemplateReadonly =>
        string.IsNullOrEmpty(Template) ? "无" : _characterData.TryRender(Template);

    public string FirstGreetingReadonly =>
        string.IsNullOrEmpty(FirstGreeting) ? "无" : _characterData.TryRender(FirstGreeting);

    public string DialogTemplateReadonly =>
        string.IsNullOrEmpty(DialogTemplate) ? "无" : _characterData.TryRender(DialogTemplate);

    public ChatPromptExecutionSettings ChatPromptExecutionSettings
    {
        get => _characterData.Config.ExecutionSettings;
        set
        {
            _characterData.Config.ExecutionSettings = value;
            OnPropertyChanged();
        }
    }

    private CharacterData _characterData;
    private AgentToolViewData? _agentTools;

    public CharacterInfoViewData() : this(new CharacterData())
    {
    }

    public CharacterInfoViewData(CharacterData characterData)
        : this(characterData, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public CharacterInfoViewData(CharacterData characterData, IMessageService messageService)
    {
        _messageService = messageService;
        _characterData = characterData;
        Name = characterData.CharacterName;
        Description = characterData.Description;

        SubAgents = new ObservableCollection<MountedAgentItem>(
            characterData.MountAgents.Select(MountedAgentItem.FromId));
        SubAgents.CollectionChanged += (sender, args) =>
        {
            _characterData.MountAgents = SubAgents.Select(x => x.Id).ToList();
        };
    }

    /// <summary>
    /// 把一个智能体挂进可委派名单
    /// </summary>
    /// <param name="character">要挂的角色;自己、非智能体档、已挂的一律忽略</param>
    public void AddSubAgent(CharacterData character)
    {
        if (character.CharacterId == CharacterId) return; //防递归
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

    public async void TryAddToNewCharacterData(Action? onSuccess = null)
    {
        ParamsSaveValidReplacer();
        if (!CharacterManager.Instance.TryAddNewCharacterData(_characterData))
        {
            if (await _messageService.ConfirmAsync(Lang.AddDuplicateCharacterTips))
                CharacterWindows.ShowEditCharacterWindow(this, x => TryAddToNewCharacterData(onSuccess));
        }
        else
        {
            _messageService.ShowNotification(
                Lang.AddCharacterSuccessTips, severity: MessageSeverity.Success);
            onSuccess?.Invoke();
        }
    }

    public bool CheckCharacterNameValid()
    {
        if (string.IsNullOrEmpty(_characterData.CharacterName))
        {
            _ = _messageService.ShowErrorAsync(Lang.CharacterEmptyNameTips);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 保存之前的参数的有效性检查并替换，避免填错参数导致的错误
    /// </summary>
    public void ParamsSaveValidReplacer()
    {
        Template = _characterData.ParamsValidReplacer(Template);
        DialogTemplate = _characterData.ParamsValidReplacer(DialogTemplate);
        FirstGreeting = _characterData.ParamsValidReplacer(FirstGreeting);
    }

    [RelayCommand]
    public void StartChat()
    {
        SessionManager.Instance.StartNewSession(_characterData);
        // agent 档角色去 agent 页:它要的工作目录、权限档与右侧栏面板只有那一页有
        App.JumpToPage(IsAgent ? MenuPages.MenuAgentKey : MenuPages.MenuChatKey);
    }

    [RelayCommand]
    public void EditCharacter()
    {
        CharacterWindows.ShowEditCharacterWindow(this, x => x.SaveCharacter());
    }

    [RelayCommand]
    public void SaveCharacter()
    {
        if (CheckCharacterNameValid())
        {
            ParamsSaveValidReplacer();
            _characterData.Save();
        }
    }

    [RelayCommand]
    public async Task DeleteCharacter()
    {
        if (await _messageService.ConfirmAsync(
                string.Format(Lang.CharacterDeleteTips, _characterData.CharacterName)))
            _characterData.Delete();
    }

    [RelayCommand]
    public async Task CopyCharacter()
    {
        if (await _messageService.ConfirmAsync(
                string.Format(Lang.CharacterCopyTips, _characterData.CharacterName)))
            _characterData.Copy();
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
