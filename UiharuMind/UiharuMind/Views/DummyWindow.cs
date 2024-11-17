/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using SharpHook.Native;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Utils;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Windows;

namespace UiharuMind.Views;

public class DummyWindow : Window
{
    // public MainWindow? MainWindow { get; private set; }
    public MainViewModel? MainViewModel { get; private set; } //=> (MainViewModel)MainWindow!.DataContext!;

    // public QuickStartChatWindow? QuickStartChatWindow { get; private set; }
    // public QuickToolWindow? QuickToolWindow { get; private set; }

    // private bool _active;

    public DummyWindow()
    {
        // SystemDecorations = SystemDecorations.None;
        // ExtendClientAreaToDecorationsHint = true;
        // ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        // var size = Screens.Primary.Bounds.Size.ToSize();
        Width = 0;
        Height = 0;
        // Position = new PixelPoint(0, 0);
        // Background = Brushes.Transparent;
        Focusable = false;
        // IsVisible = false;
        // Opacity = 0;
        this.ShowActivated = false;
        ShowInTaskbar = false;
        this.SetSimpledecorationPureWindow(false);
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Minimized;
        // this.ShowInTaskbar = false;
        // this.WindowState = WindowState.Minimized;

        // KeyDown += OnKeyDown;

        // MainWindow = LaunchMainWindow();
        this.Activated += OnActivated;
        this.Deactivated += OnDeactivated;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RegistryShortcut();
        RegistryClipboardTool();
        // Hide();

        //if(UiharuCoreManager.Instance.IsWindows) 
        // LaunchMainWindow();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        base.OnClosing(e);
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        // Log.Debug("活跃窗口" + WindowState);
        // if (_active) LaunchMainWindow();
        // _active = true;
        if (WindowState == WindowState.Minimized) LaunchMainWindow();
        WindowState = WindowState.Minimized;
        // Task.Delay(30).ContinueWith((x) => { return _active = false; });
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Log.Debug("失活窗口" + WindowState);
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

        InputManager.Instance.RegisterKey(new KeyCombinationData(KeyCode.VcS,
            LaunchQuickClipboardHistoryWindow, new List<KeyCode>()
            {
                KeyCode.VcLeftAlt, KeyCode.VcLeftShift
            },
            "Quick Clipboard History"));
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

    public void LaunchQuickClipboardHistoryWindow()
    {
        UIManager.ShowWindow<QuickClipboardHistoryWindow>();
    }
}