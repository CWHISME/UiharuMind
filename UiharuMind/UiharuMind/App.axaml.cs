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
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UiharuMind.Core;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views;
using UiharuMind.Views.Windows;
using Ursa.Controls;

namespace UiharuMind;

public partial class App : Application, ILogger, IDisposable
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // 捕获未处理的异常
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Log.Debug("UiharuMind begins to start.");
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // desktop.MainWindow = new MainWindow
            // {
            //     DataContext = new MainViewModel()
            // };
            DummyWindow = new DummyWindow();

            Clipboard = new ClipboardService(DummyWindow);
            FilesService = new FilesService();
            ScreensService = new ScreensService(DummyWindow);
            MessageService = new MessageService();
            ModelService = new ModelService();
            MemoryService = new MemoryService();

            desktop.MainWindow = DummyWindow;

            // desktop.Exit += OnExit;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();

        LogManager.Instance.Logger = this;
        Lang.Culture = CultureInfo.CurrentCulture;
        LanguageUtils.CurCultureInfo = CultureInfo.CurrentCulture;
        UiharuCoreManager.Instance.Init();

        // Process.GetCurrentProcess().Exited += OnExit;
        AppDomain.CurrentDomain.ProcessExit += OnExit;

        //强行清理可能残留的进程
        ProcessHelper.ForceClearAllProcesses();

        // var name= FontUtils.GetFontFamilyName("F:\\项目\\个人\\UiharuMind\\UiharuMind\\UiharuMind\\Assets\\Fonts\\DreamHanSansCN-W12.ttf");

        //自动打开主窗口
        DummyWindow.LaunchMainWindow();

        DeliverTrayFunc();
        Log.Debug("UiharuMind started.");
    }

    // public new static App Current => (App)Application.Current!;
    public static DummyWindow DummyWindow { get; private set; }
    public static ClipboardService Clipboard { get; private set; }
    public static FilesService FilesService { get; private set; }
    public static ScreensService ScreensService { get; private set; }
    public static ModelService ModelService { get; private set; }
    public static MemoryService MemoryService { get; private set; }
    public static MainViewModel ViewModel => DummyWindow.MainViewModel;
    public static MessageService MessageService { get; private set; }

    /// <summary>
    /// 版本号
    /// </summary>
    public static Version Version = new Version(0, 0, 4);

    public static void JumpToPage(MenuPages page)
    {
        ViewModel.JumpToPage(page);
    }

    public void Debug(string rawStr, LogItem message)
    {
        Console.WriteLine(message);
    }

    public void Warning(string rawStr, LogItem message)
    {
        Console.WriteLine(message);
    }

    public void Error(string rawStr, LogItem message)
    {
        Console.WriteLine(message);
        Dispatcher.UIThread.Post(() => MessageService.ShowErrorMessageBox(rawStr));
    }

    private static void DeliverTrayFunc()
    {
        if (Current != null)
        {
            var trayIcons = TrayIcon.GetIcons(Current);
            if (trayIcons?.Count > 0)
            {
                var trayIcon = trayIcons[0];
                trayIcon.Clicked += (x, y) => DummyWindow.LaunchMainWindow();
            }
        }
    }

    private void OnQuitClick(object? sender, EventArgs e)
    {
        Dispose();
        Process.GetCurrentProcess().Kill();
    }

    // private void OnAboutClick(object? sender, EventArgs e)
    // {
    //     
    // }

    private void OnOpenClick(object? sender, EventArgs e)
    {
        DummyWindow.LaunchMainWindow();
    }

    // private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    // {
    //     Clipboard.Dispose();
    //     ProcessHelper.CancelAll();
    // }

    private void OnExit(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 处理AppDomain级别的未处理异常
        var ex = (Exception)e.ExceptionObject;
        Log.Error(ex);
        Log.CloseAndFlush();
        if (e.IsTerminating)
        {
            Log.Error("A critical error has occurred and the application will now close.");
            Dispose();
            Environment.Exit(1);
        }
    }

    private void UIThread_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 处理UI线程上的未处理异常
        Log.Error(e.Exception);
        // 标记异常已处理
        e.Handled = true;
        Log.CloseAndFlush();
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
        Clipboard.Dispose();
        ProcessHelper.CancelAllProcesses();
    }

    private void OnScreenCaptureClick(object? sender, EventArgs e)
    {
        ScreenCaptureManager.CaptureScreen();
    }

    private void OnQuickAskClick(object? sender, EventArgs e)
    {
        DummyWindow.LaunchQuickStartChatWindow();
    }

    private void OnClipboardHistoryClick(object? sender, EventArgs e)
    {
        DummyWindow.LaunchQuickClipboardHistoryWindow();
    }

    private void OnTranslateMenuItemClick(object? sender, EventArgs e)
    {
        DummyWindow.LaunchQuickTranslationWindow();
    }

    private void OnAboutMenuItemClick(object? sender, EventArgs e)
    {
        UIManager.ShowWindow<AboutWindow>();
    }

    private void OnHelpMenuItemClick(object? sender, EventArgs e)
    {
        UIManager.ShowWindow<HelpWindow>();
    }

    private void OnOpenSaveMenuItemClick(object? sender, EventArgs e)
    {
        App.FilesService.OpenFolder(SettingConfig.SaveDataPath);
    }
}