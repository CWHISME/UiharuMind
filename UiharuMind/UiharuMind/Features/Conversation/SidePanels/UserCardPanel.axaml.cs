using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;

namespace UiharuMind.Features.Conversation.SidePanels;

public partial class UserCardPanel : UserControl
{
    public UserCardPanel()
    {
        InitializeComponent();
    }
}

/// <summary>
/// 会话详情栏的用户卡片。<b>与会话无关</b>——它包的是全局那张用户卡，
/// 只有「显不显示」跟会话有关（按角色的 InjectUserCard 定，见 ChatInfoModel）
/// </summary>
public partial class UserCardViewData : ObservableObject
{
    /// <summary>本会话要不要显示这块面板：由 ChatInfoModel 按角色的 InjectUserCard 设置</summary>
    [ObservableProperty] private bool _isShown;

    private readonly CharacterInfoViewData _user;

    public CharacterInfoViewData User => _user;

    public string UserTemplateReadonly =>
        string.IsNullOrEmpty(User.Template) ? "无" : User.Template.Replace("{{$char}}", User.Description);

    public UserCardViewData()
    {
        _user = new CharacterInfoViewData(CharacterManager.Instance.UserCharacterData);
    }

    [RelayCommand]
    public void EditUserCard()
    {
        // 编辑走草稿-提交,面板绑的这份是活实例、只读;因此关窗时统一刷一遍就够
        UIManager.ShowWindow<UserCardEditWindow>(x =>
        {
            x.SetCharacterInfo(CharacterDraft.ForEdit(CharacterManager.Instance.UserCharacterData));
            x.Closed += (_, _) =>
            {
                _user.Refresh();
                OnPropertyChanged(nameof(UserTemplateReadonly));
            };
        });
    }
}
