using System;
using Avalonia.Controls;
using UiharuMind.Core.Core.Extensions;
using Ursa.Controls;

namespace UiharuMind.Views.Common;

public class UiharuMessageBoxWindow : MessageBoxWindow
{
    private Action<MessageBoxResult>? _callback;

    public UiharuMessageBoxWindow(MessageBoxButton messageBoxButton, Action<MessageBoxResult>? callback) : base(
        messageBoxButton)
    {
        _callback = callback;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _callback?.Invoke(this.GetFieldValue<MessageBoxResult>("_dialogResult"));
    }
}