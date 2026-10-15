using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.Conversation.Single;

/// <summary>目标模式设置弹窗：目标文本 + 调度规则增删 + 开始/更新/立即唤醒/结束</summary>
public partial class GoalModeSetupWindow : Window
{
    /// <summary>加载布局</summary>
    public GoalModeSetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>打开目标模式设置弹窗</summary>
    /// <param name="goalMode">视图数据</param>
    public static Task ShowAsync(GoalModeViewData goalMode)
    {
        GoalModeSetupWindow window = new() { DataContext = goalMode };
        return window.ShowDialog(UIManager.GetFocusWindow());
    }

    /// <summary>打开一个会话的目标模式设置弹窗（入口按钮用）</summary>
    /// <param name="session">会话（agent 档）</param>
    /// <param name="messages">结束前弹确认用的消息服务</param>
    public static async Task OpenForAsync(ChatSession session, IMessageService messages)
    {
        using GoalModeViewData viewData = new(session, messages);
        await ShowAsync(viewData);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GoalModeViewData goalMode || !goalMode.StartCommand.CanExecute(null)) return;
        goalMode.StartCommand.Execute(null);
        Close();
    }

    private async void EndButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GoalModeViewData goalMode) return;
        await goalMode.ConfirmEndAsync(); //先确认再关窗，别让用户以为点下去没反应
        Close();
    }

    private void WakeNowButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GoalModeViewData goalMode) return;
        goalMode.WakeNowCommand.Execute(null);
        Close();
    }

    private void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GoalModeViewData goalMode) return;
        goalMode.UpdateCommand.Execute(null);
        // 不关窗：改完还能继续调
    }
}
