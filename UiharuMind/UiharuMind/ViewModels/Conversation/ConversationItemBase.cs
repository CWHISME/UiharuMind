/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Views;

namespace UiharuMind.ViewModels.Conversation;

/// <summary>
/// 通用会话条目基类:仅含展示与动作契约,不引用任何具体会话/角色类型,
/// 供 Agent 工作区与(后续)角色聊天共用。
/// </summary>
public abstract partial class ConversationItemBase : ObservableObject
{
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _timestamp = string.Empty;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private IBrush _senderColor = Brushes.Gray;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isDone = true;

    /// <summary>是否用户侧条目(右对齐)</summary>
    public virtual bool IsUser => false;

    /// <summary>是否系统条目(居中、无头像)</summary>
    public virtual bool IsSystem => false;

    /// <summary>内容水平对齐</summary>
    public HorizontalAlignment Alignment =>
        IsSystem ? HorizontalAlignment.Center :
        IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>头像所在列(布局 Grid 为 Auto,*,Auto)</summary>
    public int AvatarColumn => IsUser ? 2 : 0;

    /// <summary>是否显示头像</summary>
    public bool ShowAvatar => !IsSystem && Icon != null;

    /// <summary>编辑完成回调(为空则隐藏编辑按钮)</summary>
    public Action<ConversationItemBase>? EditedCallback { get; set; }

    /// <summary>删除回调(为空则隐藏删除按钮)</summary>
    public Action<ConversationItemBase>? DeleteCallback { get; set; }

    /// <summary>重试回调(为空则隐藏重试按钮)</summary>
    public Action<ConversationItemBase>? RetryCallback { get; set; }

    /// <summary>是否可编辑</summary>
    public bool CanEdit => EditedCallback != null;

    /// <summary>是否可删除</summary>
    public bool CanDelete => DeleteCallback != null;

    /// <summary>是否可重试</summary>
    public bool CanRetry => RetryCallback != null;

    [RelayCommand]
    private void Copy()
    {
        App.Clipboard.CopyToClipboard(Message, true);
    }

    [RelayCommand]
    private async Task Edit()
    {
        if (EditedCallback == null) return;
        string? result = await UIManager.ShowStringEditWindow(Message);
        if (result == null) return;
        Message = result;
        EditedCallback.Invoke(this);
    }

    [RelayCommand]
    private void Delete()
    {
        DeleteCallback?.Invoke(this);
    }

    [RelayCommand]
    private void Retry()
    {
        RetryCallback?.Invoke(this);
    }
}

/// <summary>
/// 标准文本气泡条目(支持流式追加),ConversationView 内置其模板
/// </summary>
public partial class TextConversationItem : ConversationItemBase
{
    private readonly StringBuilder _buffer = new();
    private readonly bool _isUser;

    public override bool IsUser => _isUser;

    public TextConversationItem(bool isUser)
    {
        _isUser = isUser;
    }

    /// <summary>
    /// 追加一段流式增量
    /// </summary>
    /// <param name="delta">增量文本</param>
    public void Append(string delta)
    {
        _buffer.Append(delta);
        Message = _buffer.ToString();
    }
}
