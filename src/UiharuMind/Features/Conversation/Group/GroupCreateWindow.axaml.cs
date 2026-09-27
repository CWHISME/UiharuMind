using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.Character;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>建群弹窗：挑名字、成员与调度设置。建群本身在 <c>GroupChatSessions.Create</c></summary>
public partial class GroupCreateWindow : Window
{
    public GroupCreateWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开建群弹窗
    /// </summary>
    /// <param name="isAgentGroup">是不是智能体群（跟着切换器那一侧）</param>
    /// <param name="workspacePath">智能体群的工作区；普通群为 null</param>
    /// <param name="preselected">预先勾上的成员；没有为 null</param>
    /// <param name="name">预填的群名；没有为 null</param>
    /// <returns>建群请求；取消为 null</returns>
    public static Task<GroupCreateRequest?> ShowAsync(bool isAgentGroup, string? workspacePath,
        IReadOnlyList<CharacterData>? preselected = null, string? name = null)
    {
        GroupCreateWindow window = new()
        {
            DataContext = new GroupCreateWindowModel(isAgentGroup, workspacePath, preselected, name),
        };
        return window.ShowDialog<GroupCreateRequest?>(UIManager.GetFocusWindow());
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupCreateWindowModel { CanCreate: true } model) return;
        Close(new GroupCreateRequest(model.Name.Trim(), [..model.Picked], [..model.PickedModelNames],
            model.PickedSchedule));
    }

    /// <summary>类别筛选胶囊：Tag 里放的是下标，写回模型由它重筛列表</summary>
    private void KindFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int index)
            && DataContext is GroupCreateWindowModel model)
            model.KindFilterIndex = index;
    }
}
