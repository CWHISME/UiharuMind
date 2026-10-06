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
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassicDiagnostics.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Features.Models;
using UiharuMind.Features.Memory;
using UiharuMind.Features.Clipboard;
using UiharuMind.Core.Core;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Resources.Lang;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core;
using UiharuMind.Features.ScreenCapture;
using UiharuMind.Features.QuickTools;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Utils;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind;

public partial class App : Application, ILogger, IDisposable
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // 预览器每开一个 axaml 都会起一次 App:日志转去临时目录,否则每次都轮换掉一格真实日志
        if (Design.IsDesignMode)
        {
            LogManager.UseDirectory(Path.Combine(Path.GetTempPath(), "uiharu-designer-logs"));
            return;
        }

        LogManager.Instance.Logger = this;

        // 捕获未处理的异常
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppPaths.EnsureRoot();
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("enter");
        Log.Debug("UiharuMind begins to start.");
        _exitGuard = UiharuMind.Core.Core.Diagnostics.UncleanExitGuard.Start(Debugger.IsAttached);
        // 同一档案的第二个实例不跑定时任务(ADR 0064),要早于调度后端被造出来
        if (!UiharuMind.Core.Core.Instances.AppInstance.ClaimPrimary())
        {
            Log.Debug("Another instance owns this profile's background work; scheduled tasks are read-only here.");
        }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // desktop.MainWindow = new MainWindow
            // {
            //     DataContext = new MainViewModel()
            // };
            DummyWindow = new DummyWindow();
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("dummy-window");

            // 消息服务先造出来：剪贴板、模型这些随 App 起的服务由这里递给它们，不各自回头去容器里取
            ScreensService = new ScreensService(DummyWindow);
            MessageService messages = new(ScreensService);
            Clipboard = new ClipboardService(DummyWindow, messages);
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("clipboard-service");
            FilesService = new FilesService();
            ModelService = new ModelService(messages);
            MemoryService = new MemoryService();
            UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("app-services");

            Services = new ServiceCollection()
                .AddSingleton(ScreensService)
                .AddSingleton<IMessageService>(messages)
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
        // 缩图要 SkiaSharp,只有 App 引用它;ViewImage 的预览与用户附图同一套上限(ADR 0053)
        UiharuMind.Core.AI.Execution.ViewImageTool.Downscaler =
            UiharuMind.Features.Conversation.Composer.ConversationImageDownscaler.Downscale;
        WireGroupApprovals();
        // 上次退出时还在跑的后台委派:父会话里那条「已派出」永远等不到下文,在这里补上一条中止说明
        UiharuMind.Core.AI.Execution.Tools.BackgroundSubAgentDispatcher.SettleOrphansOnStartup();
        UiharuCoreManager.Instance.Init();

        // Process.GetCurrentProcess().Exited += OnExit;
        AppDomain.CurrentDomain.ProcessExit += OnExit;

        //强行清理可能残留的进程
        // 别的实例还开着时,残留的 llama-server 可能正是它在用的本地模型
        if (UiharuMind.Core.Core.Diagnostics.UncleanExitGuard.CountOtherAlive() == 0) ProcessHelper.ForceClearAllProcesses();
        UiharuMind.Core.AI.Chat.SessionManager.Instance.WatchExternalChanges();
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
        // 引擎新版本：错开启动高峰再查，查到了在设置入口与引擎卡上提示
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await UiharuMind.Core.AI.Runtime.Backends.LLamaCppEngineInstaller.Shared.CheckForUpdateAsync();
        });
        UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Mark("update-check-kickoff");
        // 空闲内存回收:长轮次过后 GC 会占着一大片用过的空地不还,见 IdleMemoryReclaimer
        _memoryReclaimer = UiharuMind.Features.Diagnostics.IdleMemoryReclaimer.Start();

        // 开发脚本(--dev-script):没带这个参数时一行都不跑,见 DevScriptRunner
        UiharuMind.Features.DevAutomation.DevScriptRunner.RunIfRequested(
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Args);
        // 内置技能:正文每次读取时现生成,见 ADR 0061
        UiharuMind.Core.AI.Execution.Skills.SkillCatalog.Instance.RegisterBuiltIn(UiharuMind.Features.About.UiharuGuideSkill.Create());
        UiharuMind.Core.AI.Execution.Skills.SkillCatalog.Instance.RegisterBuiltIn(UiharuMind.Features.DevAutomation.UiharuDevSkill.Create());
        // 开发控制通道:开发者模式或 --dev-control 才开,见 ADR 0059
        _devControl = UiharuMind.Features.DevAutomation.DevControlHost.Start(
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Args);
        Log.Debug("UiharuMind started.");
    }

    // public new static App Current => (App)Application.Current!;
    private UiharuMind.Features.Diagnostics.IdleMemoryReclaimer? _memoryReclaimer;
    private UiharuMind.Core.Core.Diagnostics.UncleanExitGuard? _exitGuard;
    private bool _crashing; //崩溃路径上的 Dispose 不算正常退出
    private UiharuMind.Features.DevAutomation.DevControlHost? _devControl;

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

    // 普通日志只在应用内日志界面看，不再写控制台：请求体这类大正文每条都要再拼一整份、同步写 stdout
    public void Debug(string rawStr, LogItem message)
    {
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
                // 初始图标分平台:macOS 用剪影模板(App.axaml 已给 TrayFlowerIdle);Windows 先给彩色底图,
                // 否则启动瞬间黑剪影在深色任务栏上看不见
                if (!OperatingSystem.IsMacOS())
                {
                    trayIcon.Icon = IconUtils.LoadWindowIconFromAsset("TrayColorBase.png");
                }
                // 底图用 64px 彩色版:Windows 托盘实际 16px,拿 1024 主图缩到 16 会糊
                _trayStatus = new TrayStatusIndicator(trayIcon, IconUtils.AssetUri("TrayColorBase.png"));
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
        // 崩溃记录先写:它是同步直写,不受日志队列卡住的影响
        if (e.IsTerminating) CrashLog.Append(ex);
        Log.Error(ex);
        Log.Flush();
        if (e.IsTerminating)
        {
            Log.Error("A critical error has occurred and the application will now close.");
            _crashing = true;
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
        UiharuMind.Core.AI.Execution.SessionWakeTurn.ApprovalSource = WakeApprovalHosts.Resolve;
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
                        messageService.ShowNotification(Loc.Text(LangKey.SubAgentApprovalWaitingTip),
                            Loc.Text(LangKey.SubAgentApprovalWaiting), MessageSeverity.Warning,
                            TimeSpan.FromSeconds(20));
                        break;
                    case ESubAgentNotice.LongRunning:
                        messageService.ShowNotification(Loc.Text(LangKey.SubAgentLongRunning), null,
                            MessageSeverity.Warning);
                        break;
                }
            });
        };
    }

    /// <summary>
    /// 群成员开始等审批时提示用户：人不在群视图里就看不到那条待审批条，十分钟没人应就按拒绝收口
    /// </summary>
    private static void WireGroupApprovals()
    {
        UiharuMind.Core.AI.Chat.Group.HeadlessGroupMemberTurnRunner.ApprovalWaitingNotifier = member =>
        {
            if (Services?.GetService<IMessageService>() is not { } messageService) return;

            string groupTitle = member.GroupId is { } groupId
                ? UiharuMind.Core.AI.Chat.SessionManager.Instance.GetMeta(groupId)?.Title ?? string.Empty
                : string.Empty;
            Dispatcher.UIThread.Post(() => messageService.ShowNotification(Loc.Text(LangKey.GroupApprovalWaitingTip),
                string.Format(Loc.Text(LangKey.GroupApprovalWaitingFormat), member.CharacterData.CharacterName,
                    groupTitle),
                MessageSeverity.Warning, TimeSpan.FromSeconds(20)));
        };

        // 离席（ADR 0055）：取一次实例就接上了离席期间的审批通道；结束时提示一声，回执已落进群里
        GroupAwayController.Instance.Ended += receipt =>
        {
            if (Services?.GetService<IMessageService>() is not { } messageService) return;

            string groupTitle = UiharuMind.Core.AI.Chat.SessionManager.Instance.GetMeta(receipt.GroupId)?.Title ?? string.Empty;
            // 只报原因和用时/出手/发言：全文在群里的回执卡上看。注意参数顺序——第一个是限高的正文栏，
            // 第二个才是群名标题栏，写反了全文会进不限高的标题栏把通知撑爆
            string brief = GroupAwayReceiptText.FormatBrief(receipt);
            Dispatcher.UIThread.Post(() => messageService.ShowNotification(brief, groupTitle,
                MessageSeverity.Information, TimeSpan.FromSeconds(20)));
        };
    }

    //菜单栏图标的后台状态角标。静态:托盘图标是应用级的一份,装配它的那一步也是静态的
    private static TrayStatusIndicator? _trayStatus;

    public void Dispose()
    {
        _memoryReclaimer?.Dispose();
        _devControl?.Dispose();
        _trayStatus?.Dispose();
        Clipboard.Dispose();
        // 先给还在跑的那些轮次补上取消结果,再放执行者:反过来的话补写会撞上正在被释放的执行者。
        // 登记在运行侧,因此界面上的对话与无头的定时任务一并收尾
        UiharuMind.Core.AI.Execution.TurnDriver.SettleAllForShutdown();
        // 轮次都收住了才写「后台任务被中止」,不会与某一轮的落盘交错
        UiharuMind.Core.AI.Execution.Tools.BackgroundTasks.BackgroundTaskRegistry.SettleAllForShutdown();
        UiharuMind.Core.AI.Chat.SessionManager.Instance.DisposeAllRunners();
        (Services as IDisposable)?.Dispose();
        ProcessHelper.CancelAllProcesses();
        if (!_crashing) _exitGuard?.MarkClean();
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
