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
using UiharuMind.Views.Windows;
using Ursa.Controls;

namespace UiharuMind.Views;

public class DummyWindow : Window
{
    // public MainWindow? MainWindow { get; private set; }
    public MainViewModel? MainViewModel { get; private set; } //=> (MainViewModel)MainWindow!.DataContext!;

    // public QuickStartChatWindow? QuickStartChatWindow { get; private set; }
    // public QuickToolWindow? QuickToolWindow { get; private set; }

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
        RegistryClipboardTool();
        Hide();
        
        //if(UiharuCoreManager.Instance.IsWindows) 
            LaunchMainWindow();
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

        InputManager.Instance.RegisterKey(new KeyCombinationData(KeyCode.VcA,
            LaunchQuickStartChatWindow, new List<KeyCode>()
            {
                KeyCode.VcLeftAlt, KeyCode.VcLeftShift
            },
            "Quick Start Chat"));

        // RegistryShortcutQuickTool(KeyCode.VcLeftControl);
        // RegistryShortcutQuickTool(KeyCode.VcLeftAlt);
        // RegistryShortcutQuickTool(KeyCode.VcLeftMeta);
    }

    private void RegistryClipboardTool()
    {
        App.Clipboard.OnClipboardStringChanged += LaunchQuickToolWindow;
    }

    // private void RegistryShortcutQuickTool(KeyCode decorateKeyCode)
    // {
    //     InputManager.Instance.RegisterKey(new KeyCombinationData(KeyCode.VcC,
    //         LanchQuickToolWindow, new List<KeyCode>()
    //         {
    //             decorateKeyCode
    //         },
    //         "Quick Tool"));
    // }

    public void LaunchMainWindow()
    {
        MainViewModel ??= new MainViewModel();
        UIManager.ShowWindow<MainWindow>(null, x => x.DataContext = MainViewModel);
    }

    public void LaunchQuickStartChatWindow()
    {
        UIManager.ShowWindow<QuickStartChatWindow>();
    }

    public void LaunchQuickToolWindow(string answerStr)
    {
        UIManager.ShowWindow<QuickToolWindow>(x => x.SetAnswerString(answerStr));
    }
}