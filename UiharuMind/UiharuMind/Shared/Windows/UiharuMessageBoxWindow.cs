using System;
using Avalonia.Controls;
using UiharuMind.Shared.Utils;
using Ursa.Controls;
using UiharuMind.Core.Core.Extensions;

namespace UiharuMind.Shared.Windows;

public class UiharuMessageBoxWindow : MessageBoxWindow
{
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
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        MessageBoxResult? result = this.GetFieldValue<Object>("_dialogResult") as MessageBoxResult?;
        _callback?.Invoke(result ?? MessageBoxResult.None);
    }
}