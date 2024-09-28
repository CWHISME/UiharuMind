using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Common;
using Ursa.Controls;

namespace UiharuMind.Views;

public partial class MainWindow : UiharuWindowBase
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public override void Show()
    {
        base.Show();
        ShowInTaskbar = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // UiharuCoreManager.Instance.Input.Stop();
        base.OnClosing(e);
        Hide();
        e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ShowInTaskbar = false;
    }
}