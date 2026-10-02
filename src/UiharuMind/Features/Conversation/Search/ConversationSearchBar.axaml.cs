/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace UiharuMind.Features.Conversation.Search;

/// <summary>
/// 会话内搜索栏。回车跳更早一条、Shift+回车跳更新一条、Esc 关闭；绑定契约为 <see cref="ConversationSearchViewData"/>
/// </summary>
public partial class ConversationSearchBar : UserControl
{
    private ConversationSearchViewData? _viewData;

    /// <summary>构造</summary>
    public ConversationSearchBar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // 回车在 TextBox 里默认什么都不做(单行),这里接管;隧道阶段拦,免得被模板吞掉
        QueryBox.AddHandler(KeyDownEvent, OnQueryKeyDown, RoutingStrategies.Tunnel);
        // 点结果行才跳:选中在按下时已落定,已选中的那行再点一次也要能跳回去
        HitList.Tapped += (_, _) => _viewData?.JumpToSelected();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewData != null) _viewData.FocusRequested -= OnFocusRequested;
        _viewData = DataContext as ConversationSearchViewData;
        if (_viewData != null) _viewData.FocusRequested += OnFocusRequested;
    }

    private void OnFocusRequested()
    {
        // 刚从隐藏翻成可见,这一帧还没进布局,当场 Focus 落空
        Dispatcher.UIThread.Post(() =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewData == null) return;

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _viewData.Newer();
                break;
            case Key.Enter:
                _viewData.Older();
                break;
            case Key.Escape:
                _viewData.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
