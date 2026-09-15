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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Features.Models;
using UiharuMind.Features.Memory;
using UiharuMind.Features.Clipboard;
using UiharuMind.Core.Core;
using UiharuMind.Features.Conversation;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Resources.Lang;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core;
using UiharuMind.Features.ScreenCapture;
using UiharuMind.Features.QuickTools;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Utils;

namespace UiharuMind;

public partial class App : Application, ILogger, IDisposable
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        LogManager.Instance.Logger = this;

        // 捕获未处理的异常
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("enter");
        Log.Debug("UiharuMind begins to start.");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // desktop.MainWindow = new MainWindow
            // {
            //     DataContext = new MainViewModel()
            // };
            DummyWindow = new DummyWindow();
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("dummy-window");

            Clipboard = new ClipboardService(DummyWindow);
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("clipboard-service");
            FilesService = new FilesService();
            ScreensService = new ScreensService(DummyWindow);
            ModelService = new ModelService();
            MemoryService = new MemoryService();
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("app-services");

            Services = new ServiceCollection()
                .AddSingleton(ScreensService)
                .AddSingleton<IMessageService, MessageService>()
                .AddSingleton<ApplicationUpdateService>()
                .AddSingleton<MainViewModel>()
                .AddSingleton<SearchService>()
                .BuildServiceProvider();
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("di-container");
            DummyWindow.InitializeMainViewModel(Services.GetRequiredService<MainViewModel>());
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("main-viewmodel");

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
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("base-framework-init");

        LocalizationManager.Instance.InitializeFromConfig();
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("localization");
        ApplicationThemeManager.InitializeFromConfig();
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("theme");
        WireBackgroundSubAgents();
        // 上次退出时还在跑的后台委派:父会话里那条「已派出」永远等不到下文,在这里补上一条中止说明
        UiharuMind.Core.AI.Execution.Tools.BackgroundSubAgentDispatcher.SettleOrphansOnStartup();
        UiharuCoreManager.Instance.Init();

        // Process.GetCurrentProcess().Exited += OnExit;
        AppDomain.CurrentDomain.ProcessExit += OnExit;

        //强行清理可能残留的进程
        ProcessHelper.ForceClearAllProcesses();
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("clear-stale-processes");

        // var name= FontUtils.GetFontFamilyName("F:\\项目\\个人\\UiharuMind\\UiharuMind\\UiharuMind\\Assets\\Fonts\\DreamHanSansCN-W12.ttf");

        //自动打开主窗口
#if !DEBUG
        DummyWindow.LaunchMainWindow();
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("launch-main-window");
#endif
#if DEBUG
        this.AttachDevTools();
#endif

        DeliverTrayFunc();
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("tray");
        _ = Services.GetRequiredService<ApplicationUpdateService>().CheckForUpdatesAsync();
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("update-check-kickoff");
        Log.Debug("UiharuMind started.");
    }

    // public new static App Current => (App)Application.Current!;
    public static DummyWindow DummyWindow { get; private set; } = null!;
    public static ClipboardService Clipboard { get; private set; } = null!;
    public static FilesService FilesService { get; private set; } = null!;
    public static ScreensService ScreensService { get; private set; } = null!;
    public static ModelService ModelService { get; private set; } = null!;
    public static MemoryService MemoryService { get; private set; } = null!;
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainViewModel ViewModel => DummyWindow.MainViewModel!;

    /// <summary>
    /// 版本号
    /// </summary>
    public static Version Version => AppInfo.Version;

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
        if (Services?.GetService<IMessageService>() is { } messageService)
            _ = messageService.ShowErrorAsync(rawStr);
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
                // 菜单栏图标是用户切走之后唯一还看得见的东西,而审批等待是有时限的
                _trayStatus = new TrayStatusIndicator(trayIcon,
                    new Uri("avares://UiharuMind/Assets/Icon.png"));
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
        Log.Flush();
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
        Log.Flush();
    }

    /// <summary>
    /// 把后台子代理的两个注入口接到界面上。
    ///
    /// <b>都是静态口</b>：Core 里没有一条能从界面穿到工具的现成管线，而这两件事本来就是进程级的
    /// （<c>IMessageService</c> 是单例）。见 ADR 0025。
    /// </summary>
    private static void WireBackgroundSubAgents()
    {
        // 唤醒轮跑的是主代理那一轮,它要动东西时该弹给正看着它的人。
        // 取不到宿主(没开着那个会话)就按无头口径拒绝,与定时任务同形
        UiharuMind.Core.AI.Execution.Tools.BackgroundSubAgentDispatcher.WakeApprovalSource =
            WakeApprovalHosts.Resolve;
        UiharuMind.Core.AI.Execution.Tools.BackgroundSubAgentDispatcher.Notifier = (notice, _) =>
        {
            if (Services?.GetService<IMessageService>() is not { } messageService) return;

            // 「有审批在等你」是**承重**的:5 分钟没人点就按拒绝收口,那次委派基本白跑。
            // 用 Warning 是为了多留 3 秒(见 MessageService 的两档时长)
            Dispatcher.UIThread.Post(() =>
            {
                switch (notice)
                {
                    case ESubAgentNotice.ApprovalWaiting:
                        // 审批是「要你动手」那一档:读完还得去点,默认 5 秒读都读不完。
                        // 真正的常驻入口是输入区上方那条横幅与右栏面板,这条只负责「把你叫过来」
                        messageService.ShowNotification(Lang.SubAgentApprovalWaitingTip,
                            Lang.SubAgentApprovalWaiting, MessageSeverity.Warning,
                            TimeSpan.FromSeconds(20));
                        break;
                    case ESubAgentNotice.LongRunning:
                        messageService.ShowNotification(Lang.SubAgentLongRunning, null,
                            MessageSeverity.Warning);
                        break;
                }
            });
        };
    }

    //菜单栏图标的后台状态角标。静态:托盘图标是应用级的一份,装配它的那一步也是静态的
    private static TrayStatusIndicator? _trayStatus;

    public void Dispose()
    {
        _trayStatus?.Dispose();
        Clipboard.Dispose();
        // 先给还在跑的那些轮次补上取消结果,再放执行者:反过来的话补写会撞上正在被释放的执行者。
        // 登记在运行侧,因此界面上的对话与无头的定时任务一并收尾
        UiharuMind.Core.AI.Execution.TurnDriver.SettleAllForShutdown();
        UiharuMind.Core.AI.Chat.SessionManager.Instance.DisposeAllRunners();
        (Services as IDisposable)?.Dispose();
        ProcessHelper.CancelAllProcesses();
        Log.Shutdown(); //必须最后:它之后打的日志会被丢掉,而上面每一步都还在打日志
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

    private void OnSettingsMenuItemClick(object? sender, EventArgs e)
    {
        UIManager.ShowWindow<SettingsWindow>();
    }

    private void OnOpenSaveMenuItemClick(object? sender, EventArgs e)
    {
        App.FilesService.OpenFolder(AppPaths.Root);
    }

    private void OnAutoClickMenuItemClick(object? sender, EventArgs e)
    {
        DummyWindow.LaunchQuickAutoClickWindow();
    }
}
