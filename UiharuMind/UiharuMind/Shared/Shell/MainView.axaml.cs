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

namespace UiharuMind.Shared.Shell;

public partial class MainView : UserControl
{
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
            active = viewControl.View;
            // ContentControl 时代页面靠"内容即数据上下文"隐式继承,常驻宿主必须显式赋值
            if (!ReferenceEquals(active.DataContext, _viewModel.Content)) active.DataContext = _viewModel.Content;
            if (!PageHost.Children.Contains(active)) PageHost.Children.Add(active);
        }

        foreach (Control child in PageHost.Children)
        {
            child.IsVisible = ReferenceEquals(child, active);
        }
    }
}
