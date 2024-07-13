using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels;

namespace UiharuMind.Views;

public class DummyWindow : Window
{
    public MainWindow MainWindow { get; private set; }

    public DummyWindow()
    {
        Background = Brushes.Transparent;
        Focusable = false;
        IsVisible = false;
        this.ShowInTaskbar = false;
        // this.WindowState = WindowState.Minimized;

        KeyDown += OnKeyDown;

        MainWindow = LaunchMainWindow();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Hide();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        Log.Debug("按下：" + e.Key);
    }

    private MainWindow LaunchMainWindow()
    {
        var mainWindow = new MainWindow() { DataContext = new MainViewModel() };
        // Dispatcher.UIThread.Post(mainWindow.Show);
        return mainWindow;
    }
}