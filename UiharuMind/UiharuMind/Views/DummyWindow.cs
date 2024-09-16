using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using SharpHook.Native;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.ScreenCaptures;
using Ursa.Controls;

namespace UiharuMind.Views;

public class DummyWindow : Window
{
    public MainWindow? MainWindow { get; private set; }
    public MainViewModel MainViewModel => (MainViewModel)MainWindow!.DataContext!;

    public DummyWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        Width = 1;
        Height = 1;
        Position = new PixelPoint(0, 0);
        Background = Brushes.Transparent;
        Focusable = false;
        IsVisible = false;
        // this.ShowInTaskbar = false;

        // this.WindowState = WindowState.Minimized;

        // KeyDown += OnKeyDown;

        // MainWindow = LaunchMainWindow();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RegistryShortcut();
        Hide();
    }

    // private void OnKeyDown(object? sender, KeyEventArgs e)
    // {
    //     Log.Debug("按下：" + e.Key);
    // }

    private void RegistryShortcut()
    {
        InputManager.Instance.RegisterKey(new KeyCombinationData(KeyCode.VcZ,
            ScreenCaptureManager.CaptureScreen, new List<KeyCode>()
            {
                KeyCode.VcLeftAlt, KeyCode.VcLeftShift
            },
            "Capture Screen"));
    }

    public void LaunchMainWindow()
    {
        if (MainWindow == null) MainWindow = new MainWindow() { DataContext = new MainViewModel() };
        // Dispatcher.UIThread.Post(mainWindow.Show);
        MainWindow.Show();
    }
}