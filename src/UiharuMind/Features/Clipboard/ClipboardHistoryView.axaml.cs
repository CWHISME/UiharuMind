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
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Clipboard;

namespace UiharuMind.Features.Clipboard;

public partial class ClipboardHistoryView : UserControl
{
    private const double NextPageThreshold = 400; //离底多少像素开始续页
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(300);

    private readonly IMessageService _messageService;
    private DispatcherTimer? _hoverTimer;

    public ClipboardHistoryView()
    {
        _messageService = App.Services.GetRequiredService<IMessageService>();
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<ClipboardHistoryViewModel>();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>().SyncData();
        HistoryListBox.AddHandler(ScrollViewer.ScrollChangedEvent, OnHistoryScrollChanged);
    }

    // 快滚到底时续取下一页。列表是分页加载的,历史没有上限,不可能一次全读进来
    private void OnHistoryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        if (viewer.Offset.Y < viewer.Extent.Height - viewer.Viewport.Height - NextPageThreshold) return;
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>().LoadNextPage();
    }

    // 悬停取全文要防抖:鼠标从列表上扫过一趟就是几十次读盘
    private void Item_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ClipboardItem item) return;

        _hoverTimer?.Stop();
        _hoverTimer = new DispatcherTimer { Interval = HoverDelay };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer?.Stop();
            _ = item.LoadTooltipAsync();
        };
        _hoverTimer.Start();
    }

    // private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    // {
    //     if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
    //     App.ViewModel.GetViewModel<ClipboardHistoryViewModel>()
    //         .Copy((ClipboardItem)((Control)(e.Source!))!.DataContext!);
    // }

    private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>()
            .Copy((ClipboardItem)((Control)(e.Source!))!.DataContext!);
    }

    private void MenuItemDelete_Click(object? sender, RoutedEventArgs e)
    {
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>()
            .Delete((ClipboardItem)((Control)(e.Source!))!.DataContext!);
    }

    private void MenuItemToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>()
            .ToggleFavorite((ClipboardItem)((Control)(e.Source!))!.DataContext!);
    }

    private async void MenuItemDeleteAll_Click(object? sender, RoutedEventArgs e)
    {
        if (await _messageService.ConfirmAsync(Loc.Text(LangKey.DeleteAllClipboardHistoryTips)))
        {
            App.ViewModel.GetViewModel<ClipboardHistoryViewModel>().DeleteAll();
        }
    }

    private void SearchToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var toggleButton = (ToggleButton)sender;
        bool isChecked = toggleButton.IsChecked == true;

        if (isChecked)
        {
            SearchTextBox.Focus();
        }
    }

    private void SearchTextBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchToggleButton.IsChecked = false;
        }
    }
}
