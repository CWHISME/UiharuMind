using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.Core.SimpleLog;
using Ursa.Controls;

namespace UiharuMind.Views;

public partial class MainWindow : Window
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