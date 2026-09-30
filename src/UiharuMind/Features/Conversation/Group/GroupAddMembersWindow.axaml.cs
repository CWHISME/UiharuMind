using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>建群之后加人（含加回）的弹窗：挑人与补历史的方式。改名单本身在 <c>GroupMembership</c></summary>
public partial class GroupAddMembersWindow : Window
{
    public GroupAddMembersWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开加人弹窗
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="preselected">预先勾上的角色；没有为 null</param>
    /// <returns>加人请求；取消为 null</returns>
    public static Task<GroupAddRequest?> ShowAsync(ChatSession group, CharacterData? preselected = null)
    {
        GroupAddMembersWindow window = new()
        {
            DataContext = new GroupAddMembersWindowModel(group, preselected),
        };
        return window.ShowDialog<GroupAddRequest?>(UIManager.GetFocusWindow());
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupAddMembersWindowModel { CanAdd: true } model) return;
        Close(model.ToRequest());
    }
}
