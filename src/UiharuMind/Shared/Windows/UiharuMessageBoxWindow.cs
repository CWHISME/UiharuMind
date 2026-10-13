using System;
using System.Reflection;
using Avalonia.Controls;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using Ursa.Controls;

namespace UiharuMind.Shared.Windows;

public class UiharuMessageBoxWindow : MessageBoxWindow
{
    // 非模态打开时 Avalonia 不公开 Close(result) 的结果，只能读这个私有字段；常量名的 GetField 能被 AOT 静态认出并保留
    private static readonly FieldInfo? DialogResultField =
        typeof(Window).GetField("_dialogResult", BindingFlags.Instance | BindingFlags.NonPublic);

    private Action<MessageBoxResult>? _callback;

    public UiharuMessageBoxWindow(MessageBoxButton messageBoxButton, Action<MessageBoxResult>? callback) : base(
        messageBoxButton)
    {
        _callback = callback;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.SetScreenCenterPosition();
        OverlayWindowService.PreventTopmostNativeFullScreen(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        MacWindowFocusGuard.SuppressKeyHandoff(this);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        MessageBoxResult? result = DialogResultField?.GetValue(this) as MessageBoxResult?;
        _callback?.Invoke(result ?? MessageBoxResult.None);
    }
}