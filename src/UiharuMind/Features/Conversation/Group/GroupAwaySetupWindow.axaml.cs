using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>化身设置弹窗：目标、重要提醒、化身模型与无限模式。数据上下文是右栏离席块共用的那一份视图数据</summary>
public partial class GroupAwaySetupWindow : Window
{
    /// <summary>加载布局</summary>
    public GroupAwaySetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开化身设置弹窗
    /// </summary>
    /// <param name="away">右栏离席块的视图数据（弹窗与单行共用，改完即生效）</param>
    public static Task ShowAsync(GroupAwayViewData away)
    {
        GroupAwaySetupWindow window = new() { DataContext = away };
        return window.ShowDialog(UIManager.GetFocusWindow());
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupAwayViewData away || !away.StartCommand.CanExecute(null)) return;
        away.StartCommand.Execute(null);
        Close();
    }

    private async void EndButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupAwayViewData away) return;
        await away.ConfirmEndAsync(); //先确认再关窗，别让用户以为点下去没反应
        Close();
    }

    private void WakeNowButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupAwayViewData away) return;
        away.WakeNowCommand.Execute(null);
        Close();
    }

    private void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupAwayViewData away) return;
        away.UpdateCommand.Execute(null);
        // 不关窗：改完还能继续调（比如先改提醒再开无限模式）
    }
}
