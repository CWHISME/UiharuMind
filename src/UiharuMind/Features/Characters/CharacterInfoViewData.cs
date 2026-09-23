using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色库里的一项：<b>只读展示 + 三个动作</b>（开始对话 / 复制 / 删除）。
///
/// 它直接挂活实例，因此永远显示的是<b>已提交</b>的样子。改角色是另一个类型
/// <see cref="CharacterDraft"/> 的事——两者拆开正是为了让「编辑到一半」不会漏到列表上：
/// 从前一个类既当列表项又当表单，setter 直接写进活实例，于是没点保存也已经改完了。
/// </summary>
public partial class CharacterInfoViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly CharacterData _characterData;

    /// <summary>本项对应的角色实例。要编辑它就拿它造一份 <see cref="CharacterDraft"/></summary>
    public CharacterData Data => _characterData;

    /// <summary>
    /// 角色标识（挂载引用与会话引用都用它，改名不会断链）
    /// </summary>
    public string CharacterId => _characterData.CharacterId;

    public bool IsDefault => _characterData.IsDefaultCharacter;

    /// <summary>是否为智能体（决定「开始对话」切到哪一类）。这是角色身份的唯一轴（ADR 0043）</summary>
    public bool IsAgent => _characterData.IsAgent;

    /// <summary>类别显示名(列表徽章)</summary>
    public string KindName => CharacterKindPresentation.NameOf(_characterData);

    /// <summary>类别徽章底色</summary>
    public IImmutableSolidColorBrush KindColor => CharacterKindPresentation.ColorOf(_characterData);

    /// <summary>存档时间，列表排序用</summary>
    public long FileDateTime => _characterData.FileDateTime;

    public string Name => _characterData.CharacterName;

    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(_characterData);

    public string Description => _characterData.Description;

    /// <summary>提示词正文。用户卡面板要按只读方式回显它</summary>
    public string Template => _characterData.Template;

    /// <summary>搜索用的比对文本（名字 + 描述）</summary>
    public string SearchText => $"{Name}\n{Description}";

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
    }

    /// <summary>
    /// 重新读一遍活实例。编辑提交是往同一个实例上盖，实例不换、绑定收不到通知，
    /// 因此由 <c>CharacterManager.OnCharacterUpdated</c> 的订阅方喊这一声。
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Template));
        OnPropertyChanged(nameof(IsAgent));
        OnPropertyChanged(nameof(KindName));
        OnPropertyChanged(nameof(KindColor));
        OnPropertyChanged(nameof(SearchText));
    }

    [RelayCommand]
    public void StartChat()
    {
        ChatSession session = SessionManager.Instance.StartNewSession(_characterData);
        // 对话页已合并：agent 档与普通档同页，跳过去后直达对应类型并选中刚建的会话
        App.JumpToPage(MenuPages.MenuConversationKey);
        if (App.ViewModel is MainViewModel vm && vm.Content is ConversationPageData page)
            page.RevealSession(session.SessionId, IsAgent ? EConversationType.Agent : EConversationType.Chat);
    }

    [RelayCommand]
    public async Task DeleteCharacter()
    {
        if (await _messageService.ConfirmAsync(
                Loc.Text(LangKey.CharacterDeleteTips, _characterData.CharacterName)))
            _characterData.Delete();
    }

    [RelayCommand]
    public async Task CopyCharacter()
    {
        if (await _messageService.ConfirmAsync(
                Loc.Text(LangKey.CharacterCopyTips, _characterData.CharacterName)))
            _characterData.Copy();
    }
}
