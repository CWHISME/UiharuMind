/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UiharuMind.ViewModels.Conversation;

/// <summary>
/// 通用会话视图模型基类:ConversationView 的绑定契约。
/// 不引用任何具体会话类型;发送/停止由子类实现。
/// </summary>
public abstract partial class ConversationViewModelBase : ViewModelBase
{
    public ObservableCollection<ConversationItemBase> Items { get; } = new();

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _inputPlaceholder = string.Empty;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _scrollToEnd;
    [ObservableProperty] private KeyGesture _sendGesture = new(Key.Enter);

    [RelayCommand]
    private async Task SendMessage()
    {
        string text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = string.Empty;
        await SendCoreAsync(text);
    }

    [RelayCommand]
    private void StopSending()
    {
        OnStopSending();
    }

    [RelayCommand]
    private void InputExtra()
    {
        OnInputExtra();
    }

    /// <summary>
    /// 输入框附加手势(Tab)回调,默认无行为;宿主可用于模式切换等
    /// </summary>
    protected virtual void OnInputExtra()
    {
    }

    /// <summary>
    /// 发送一条用户输入
    /// </summary>
    /// <param name="text">输入文本</param>
    protected abstract Task SendCoreAsync(string text);

    /// <summary>
    /// 停止当前生成
    /// </summary>
    protected abstract void OnStopSending();
}
