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
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Interfaces;
using UiharuMind.Shared.Diagnostics;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Shell;

public partial class MainView : UserControl
{
    private MainViewModel? _viewModel;

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

}
