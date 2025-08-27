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
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SharpHook.Native;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Utils;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows;
using Ursa.Controls;

namespace UiharuMind.Views;

public class DummyWindow : Window
{
    // public MainWindow? MainWindow { get; private set; }
    public MainViewModel? MainViewModel { get; private set; } //=> (MainViewModel)MainWindow!.DataContext!;

    // public QuickStartChatWindow? QuickStartChatWindow { get; private set; }
    // public QuickToolWindow? QuickToolWindow { get; private set; }

    // private bool _isMainWindowActive;
    // private Window? _miniDummyWindow;

    // private bool _isInit;

    public DummyWindow()
    {
        // SystemDecorations = SystemDecorations.None;
        // ExtendClientAreaToDecorationsHint = true;
        // ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        // var size = Screens.Primary.Bounds.Size.ToSize();
        Width = 1;
        Height = 1;
        // Position = new PixelPoint(0, 0);
        // Background = Brushes.Transparent;
        Focusable = true;
        // IsVisible = false;
        // Opacity = 0;
        this.ShowActivated = false;
        ShowInTaskbar = false;
        this.SetSimpledecorationPureWindow(false);
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Normal;
        // this.ShowInTaskbar = false;
        // this.WindowState = WindowState.Minimized;

        // KeyDown += OnKeyDown;

        // MainWindow = LaunchMainWindow();
        // this.Activated += OnActivated;
        // this.GotFocus += OnActivated;
        // this.Deactivated += OnDeactivated;
    }

    // protected override void OnGotFocus(GotFocusEventArgs e)
    // {
    //     base.OnGotFocus(e);
    //     OnActivated(sender: this, e: e);
    // }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        InputManager.Instance.Start(OnQuickKeyInitFailure);
        RegistryShortcut();
        RegistryClipboardTool();
        // Hide();

        //if(UiharuCoreManager.Instance.IsWindows) 
        // LaunchMainWindow();
        // _isInit = true;
    }

    private void OnQuickKeyInitFailure()
    {
        Dispatcher.UIThread.Post(() =>
        {
            App.MessageService.ShowMessageBox(
                "Failed to bind system shortcut keys. If you are using a Mac, please ensure you have the required permissions and follow the instructions provided, then try again!\n\nNote:Please be aware that if the permission list is already in place but is not functioning correctly, you should first remove the permissions and then re-add them.",
                "UiharuMind: Ops!", MessageBoxIcon.Warning,
                MessageBoxButton.OK, (x) => { InputManager.Instance.Start(OnQuickKeyInitFailure); });
        });
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

        // if (_isMainWindowActive && !UIManager.IsClosing) LaunchMainWindow();
        // if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        // {
        //     if (desktop.Windows.FirstOrDefault(x => x != this && x is not UiharuWindowBase) != null) return;
        //     // Log.Debug("活跃窗口" + desktop.Windows.Count);
        // }
        if (!UIManager.IsClosing) LaunchMainWindow();
        //想点击 Mac 托盘打开主界面，但是有问题，会导致关闭上一个界面时也自动将主界面打开，难顶！
        // if (WindowState == WindowState.Minimized)
        // {
        //     WindowState = WindowState.Normal;
        //     LaunchMainWindow();
        // }
        // else WindowState = WindowState.Minimized;

        // if (WindowState == WindowState.Minimized && !UIManager.IsClosing)
        // {
        //     WindowState = WindowState.Normal;
        //     LaunchMainWindow();
        // }
        // else WindowState = WindowState.Minimized;
        // UIManager.IsClosing = false;

        // if (_isActive||UIManager.IsClosing)
        // {
        //     _isActive = false;
        //     //关闭界面必然触发主界面 Active，额外处理
        //     UIManager.IsClosing = false;
        //     return;
        // }
        //
        // // _isActive = true;
        // LaunchMainWindow();

        // Task.Delay(30).ContinueWith((x) => { return _active = false; });
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // _isActive = false;
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

        InputManager.Instance.RegisterKey(new KeyCombinationData(KeyCode.VcQ,
            LaunchQuickTranslationWindow, new List<KeyCode>()
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
        App.Clipboard.OnClipboardImageChanged += LaunchQuickImagePinWindow;
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
        UIManager.ShowWindow<MainWindow>(null, x =>
        {
            x.DataContext = MainViewModel;
            // x.Activated += OnMainWindowActivated;
            // x.OnPreCloseEvent += OnMainWindowClosed;
        });
    }

    // private void OnMainWindowActivated(object? sender, EventArgs e)
    // {
    //     _isMainWindowActive = true;
    //
    //     // if (_miniDummyWindow != null) return;
    //     // _miniDummyWindow = new Window
    //     // {
    //     //     Width = 1,
    //     //     Height = 1,
    //     //     ShowInTaskbar = false
    //     // };
    //     // _miniDummyWindow.Show();
    // }
    //
    // private void OnMainWindowClosed()
    // {
    //     _isMainWindowActive = false;
    // }

    public void LaunchQuickStartChatWindow()
    {
        QuickStartChatWindow.Show("");
    }

    public void LaunchQuickToolWindow(string answerStr)
    {
        QuickToolWindow.Show(answerStr);
    }

    private void LaunchQuickImagePinWindow(Bitmap obj)
    {
        QuickPinImageTipWindow.Show(obj);
    }

    public void LaunchQuickClipboardHistoryWindow()
    {
        UIManager.ShowWindow<QuickClipboardHistoryWindow>();
    }

    public void LaunchQuickTranslationWindow()
    {
        UIManager.ShowWindow<QuickTranslationWindow>();
    }
}