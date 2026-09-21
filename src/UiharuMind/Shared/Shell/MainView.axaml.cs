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
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Interfaces;
using UiharuMind.Shared.Diagnostics;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Shell;

public partial class MainView : UserControl
{
    /// <summary>
    /// 空闲时预建的页面。<b>不含智能体页与对话页</b>——它们的 <c>PageData</c> 构造会经
    /// <c>SwitchConversation</c> 触发会话加载，启动时预热等于顺带拉起 MCP 子进程
    /// </summary>
    private static readonly MenuPages[] PrewarmPages =
    [
        MenuPages.MenuCharacterKey,
        MenuPages.MenuModelKey,
        MenuPages.MenuServicesKey,
        MenuPages.MenuLogKey,
    ];

    /// <summary>第一页预热的起始延迟。给启动留出安静下来的时间</summary>
    private static readonly TimeSpan PrewarmDelay = TimeSpan.FromSeconds(3);

    private MainViewModel? _viewModel;
    private int _prewarmIndex;
    private bool _isPrewarmScheduled;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        UiStallProbe.Start();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as MainViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RefreshActivePage();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Content)) RefreshActivePage();
    }

    /// <summary>
    /// 页面切换只切可见性:页面视图首次使用后常驻视觉树,
    /// 避免 ContentControl 换页导致整棵子树脱挂/重挂(长会话页大量气泡时重挂需重新套样式与布局)
    /// </summary>
    private void RefreshActivePage()
    {
        Control? active = null;
        if (_viewModel?.Content is IViewControl viewControl)
        {
            long viewCtorBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
            active = viewControl.View;
            global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End("page/view-ctor", viewCtorBegin);
            // ContentControl 时代页面靠"内容即数据上下文"隐式继承,常驻宿主必须显式赋值
            if (!ReferenceEquals(active.DataContext, _viewModel.Content)) active.DataContext = _viewModel.Content;
            AttachSharedView(PageHost, active);
        }

        foreach (Control child in PageHost.Children)
        {
            child.IsVisible = ReferenceEquals(child, active);
        }

        // 当场把目标页的布局跑完,而不是等下一轮:第一帧画出来的就是最终几何,
        // 顺带让探针量到的 layout 真正归因于这一次切页
        if (active != null && PageSwitchPerfProbe.IsSwitchPending)
        {
            long layoutBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
            PageSwitchPerfProbe.BeginLayout(active, this);
            active.UpdateLayout();
            PageSwitchPerfProbe.ReportRendered();
            global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End("page/layout", layoutBegin);
        }

        if (active == null) return;

        SchedulePrewarm();
        if (_viewModel != null) PageSwitchBench.Start(_viewModel);
    }

    /// <summary>
    /// 把常驻页面视图挂到指定宿主。视图是单例、随 <c>MainViewModel</c> 常驻，
    /// 而窗口不是：旧主窗口被缓存淘汰/超时真关后，视图还挂在那个已死的 <c>PageHost</c> 上。
    /// 只查自己名下有没有就直接 <c>Add</c>，新窗口第一次挂载必炸
    /// （<c>InvalidOperationException: already has a visual parent</c>，新旧宿主同名都是
    /// <c>PageHost</c>，报错看起来像自己跟自己冲突）。先从旧宿主摘下再挂。
    /// </summary>
    internal static void AttachSharedView(Panel host, Control view)
    {
        if (host.Children.Contains(view)) return;
        if (view.Parent is Panel oldHost && !ReferenceEquals(oldHost, host)) oldHost.Children.Remove(view);
        host.Children.Add(view);
    }

    /// 第一页预热要等启动彻底安静下来才开始。实测启动后头两秒 UI 线程已经被占满
    /// (最长一次掉帧 1.5 秒),那时候插进去建页面只会让"刚打开就卡"更严重;
    /// 而首次访问那 58~209ms 每页每次运行只付一次,晚几秒付掉毫无损失。
    /// 后续几页排在 SystemIdle 上——比 ApplicationIdle 更靠后,谁都排在它前面
    private void SchedulePrewarm()
    {
        if (_isPrewarmScheduled || _prewarmIndex >= PrewarmPages.Length) return;
        _isPrewarmScheduled = true;

        if (_prewarmIndex == 0)
        {
            DispatcherTimer.RunOnce(PrewarmNext, PrewarmDelay, DispatcherPriority.SystemIdle);
            return;
        }

        Dispatcher.UIThread.Post(PrewarmNext, DispatcherPriority.SystemIdle);
    }

    /// 一次只预建一页,建完再排下一轮:预热本身要跑整窗布局,连着做会把空闲期占死
    private void PrewarmNext()
    {
        _isPrewarmScheduled = false;
        if (_viewModel == null || _prewarmIndex >= PrewarmPages.Length) return;

        MenuPages key = PrewarmPages[_prewarmIndex++];
        try
        {
            PrewarmPage(_viewModel.GetPage(key));
        }
        catch (Exception e)
        {
            // 预热纯属提前付账,失败了让首次切页照常自己建
            Log.Warning($"Prewarm page {key} failed: {e.Message}");
        }

        SchedulePrewarm();
    }

    /// <summary>
    /// 提前把一页的视觉树建好。
    ///
    /// 模板展开与样式套用发生在<b>首次 measure</b>，而 <c>IsVisible=false</c> 的子树根本不布局——
    /// 所以预热必须让它短暂可见：透明挂上、当场跑完布局、再收回去。整段落在同一个派发任务里，
    /// 中间不会渲染出一帧。之后真正切过去只剩一次重排（实测 &lt;50ms）。
    /// </summary>
    /// <param name="viewControl">目标页</param>
    private void PrewarmPage(IViewControl viewControl)
    {
        Control view = viewControl.View;
        if (PageHost.Children.Contains(view)) return;

        if (!ReferenceEquals(view.DataContext, viewControl)) view.DataContext = viewControl;
        view.Opacity = 0;
        view.IsVisible = true;
        AttachSharedView(PageHost, view);
        view.UpdateLayout();
        view.IsVisible = false;
        view.Opacity = 1;
    }
}
