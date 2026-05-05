using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SharpHook.Data;
using UiharuMind.Core.AutoClick;
using UiharuMind.Core.Input;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows.AutoClick;
using Ursa.Controls;

namespace UiharuMind.Views.Windows;

public partial class QuickAutoClickWindow : UiharuWindowBase
{
    private QuickAutoClickViewModel _viewModel;
    private RecordingIndicatorWindow? _indicatorWindow;
    private PlaybackIndicatorWindow? _playbackWindow;

    public QuickAutoClickWindow()
    {
        InitializeComponent();

        _viewModel = App.ViewModel.GetViewModel<QuickAutoClickViewModel>();
        _viewModel.SetRecordingCallbacks(PrepareForRecording, FinishRecording);
        _viewModel.SetPlaybackCallback(UpdatePlaybackIndicator, FinishPlayback);
        _viewModel.OnActionRecorded += UpdateRecordingIndicator;
        DataContext = _viewModel;
    }

    public override bool IsCacheWindow => true;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // 窗口打开时的初始化逻辑
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        _viewModel.StopRecordingCommand.Execute(null);
        _viewModel.StopPlaybackCommand.Execute(null);
        _viewModel.OnDisable();
        _indicatorWindow?.Close();
        _playbackWindow?.Close();
    }

    /// <summary>
    /// 开始录制前显示指示器窗口
    /// </summary>
    public void PrepareForRecording()
    {
        UIManager.ShowWindow<RecordingIndicatorWindow>(x =>
        {
            _indicatorWindow = x;
            SafeClose();
        });
        // if (_indicatorWindow == null)
        // {
        //     _indicatorWindow = new RecordingIndicatorWindow();
        // }
        // _indicatorWindow.Show();
        // this.Hide();
    }

    /// <summary>
    /// 停止录制后关闭指示器并显示主窗口
    /// </summary>
    public void FinishRecording()
    {
        _indicatorWindow?.SafeClose();
        _indicatorWindow = null;
        // this.Show();
        // this.Activate();
    }

    /// <summary>
    /// 更新录制的动作计数
    /// </summary>
    public void UpdateRecordingIndicator(int count)
    {
        _indicatorWindow?.UpdateActionCount(count);
        _indicatorWindow?.FlashIndicator();
    }

    /// <summary>
    /// 更新回放进度指示器
    /// </summary>
    public void UpdatePlaybackIndicator(int current, int total)
    {
        if (_playbackWindow == null)
        {
            _playbackWindow = new PlaybackIndicatorWindow();
            _playbackWindow.Show();
            this.Hide();
        }

        _playbackWindow.UpdateProgress(current, total);
        _playbackWindow.FlashIndicator();
    }

    /// <summary>
    /// 完成回放，关闭指示器并显示主窗口
    /// </summary>
    private void FinishPlayback()
    {
        _playbackWindow?.Close();
        _playbackWindow = null;
        this.Show();
        this.Activate();
    }

    /// <summary>
    /// 是否正在录制（公开属性）
    /// </summary>
    public bool IsRecording => _viewModel.IsRecording;

    /// <summary>
    /// 显示按键选择对话框
    /// </summary>
    private void ShowKeySelectionDialog()
    {
        var dialog = new Window
        {
            Title = "选择按键",
            Width = 350,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Topmost = true,
            Background = Avalonia.Media.Brushes.White
        };

        var instructionText = new TextBlock
        {
            Text = "请按下想要添加的按键\n(按 ESC 取消)",
            FontSize = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Avalonia.Media.Brushes.Gray
        };

        var keyDisplayText = new TextBlock
        {
            Text = "等待按键...",
            FontSize = 24,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = Avalonia.Media.Brushes.Blue
        };

        var panel = new StackPanel
        {
            Children = { instructionText, keyDisplayText },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        dialog.Content = panel;

        // 使用 InputManager 监听按键
        void OnKeyDownHandler(KeyCode keyCode)
        {
            if (keyCode == KeyCode.VcEscape)
            {
                dialog.Close();
                return;
            }

            // 忽略修饰键
            if (!IsModifierKey(keyCode))
            {
                _viewModel.AddCustomKeyAction(keyCode);
                dialog.Close();
            }
        }

        bool IsModifierKey(KeyCode keyCode)
        {
            return keyCode is KeyCode.VcLeftShift or KeyCode.VcRightShift or
                KeyCode.VcLeftControl or KeyCode.VcRightControl or
                KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
                KeyCode.VcLeftMeta or KeyCode.VcRightMeta;
        }

        // 注册临时监听器
        InputManager.Instance.EventOnKeyDown += OnKeyDownHandler;
        dialog.Opened += (s, e) =>
        {
            keyDisplayText.Text = "等待按键...";
            dialog.Focus();
        };
        dialog.Closing += (s, e) => { InputManager.Instance.EventOnKeyDown -= OnKeyDownHandler; };

        dialog.ShowDialog(this);
    }

    private void AddKeyButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowKeySelectionDialog();
    }

    /// <summary>
    /// 会话列表选择变化
    /// </summary>
    private async void SessionList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        var session = e.AddedItems[0] as AutoClickSession;
        if (session == null) return;

        // 检查是否有未保存的修改
        if (_viewModel.HasUnsavedChanges)
        {
            var result = await App.MessageService.ShowConfirmMessageBox(
                "当前会话有未保存的修改，是否先保存？", 
                this);
            
            if (result == MessageBoxResult.Yes)
            {
                // 用户选择保存
                _viewModel.SaveSession();
            }
            else if (result == MessageBoxResult.Cancel)
            {
                // 用户取消操作，恢复之前的选中项
                var listBox = sender as ListBox;
                if (listBox != null)
                {
                    // 清除当前的选中，恢复到之前的状态
                    listBox.SelectedIndex = -1;
                }
                return;
            }
            // 如果选择 No，直接继续加载新会话（丢弃修改）
        }

        // 加载选中的会话
        _viewModel.LoadSession(session);
    }
    
    /// <summary>
    /// 新建会话按钮点击
    /// </summary>
    private async void NewSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        // 如果有未保存的修改，提示
        if (_viewModel.HasUnsavedChanges)
        {
            var result = await App.MessageService.ShowConfirmMessageBox(
                "当前会话有未保存的修改，是否先保存？", 
                this);
            
            if (result == MessageBoxResult.Yes)
            {
                // 选择保存
                _viewModel.SaveSession();
            }
            else if (result == MessageBoxResult.Cancel)
            {
                // 取消操作
                return;
            }
            // 如果选择 No，直接继续创建新会话
        }
        
        _viewModel.NewSession();
    }
    
    /// <summary>
    /// 保存会话按钮点击
    /// </summary>
    private async void SaveSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        // 如果没有当前会话，需要先命名
        if (_viewModel.CurrentSession == null)
        {
            var name = await UIManager.ShowStringEditWindow("新会话");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            
            // 创建新会话并设置名称
            _viewModel.NewSession();
        }
        
        // 执行保存
        _viewModel.SaveSession();
    }

    /// <summary>
    /// 重命名会话菜单项点击
    /// </summary>
    private async void RenameSessionMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem)
        {
            if (((Control)(e.Source!))!.DataContext is AutoClickSession session)
            {
                var result = await UIManager.ShowStringEditWindow(session.Name);

                if (!string.IsNullOrWhiteSpace(result) && result != session.Name)
                {
                    AutoClickManager.Instance.ModifyName(session, result);
                    _viewModel.StatusText = $"✏️ 已重命名为: {result}";
                }
            }
        }
    }

    /// <summary>
    /// 删除会话菜单项点击
    /// </summary>
    private async void DeleteSessionMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem)
        {
            // 直接从 e.Source 获取 Grid 的 DataContext
            if (((Control)(e.Source!))!.DataContext is AutoClickSession session)
            {
                // 如果会话有动作，需要确认
                if (session.Actions.Count > 0)
                {
                    var result = await App.MessageService.ShowConfirmMessageBox(
                        $"确定要删除会话 \"{session.Name}\" 吗？\n该会话包含 {session.Actions.Count} 个动作。", 
                        this);
                    
                    if (result != MessageBoxResult.Yes)
                        return;
                }
                
                // 空会话直接删除，不需要确认
                _viewModel.DeleteSession(session);
            }
        }
    }
    
    /// <summary>
    /// 删除按钮点击（从条目中）
    /// </summary>
    private async void DeleteSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is AutoClickSession session)
        {
            // 如果会话有动作，需要确认
            if (session.Actions.Count > 0)
            {
                var result = await App.MessageService.ShowConfirmMessageBox(
                    $"确定要删除会话 \"{session.Name}\" 吗？\n该会话包含 {session.Actions.Count} 个动作。", 
                    this);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            // 空会话直接删除，不需要确认
            _viewModel.DeleteSession(session);
        }
    }
}