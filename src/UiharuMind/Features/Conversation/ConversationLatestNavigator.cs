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
using Avalonia.Threading;
using UiharuMind.Shared.Controls;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 消息流「往最新那头」的视图一侧：右下角常驻箭头的显隐与点击、停在旧消息那段（脱离末尾）时
/// 滚到底往后续批、跟底随脱离暂停与恢复（见 ADR 0056）。与 <see cref="ConversationSearchJump"/> 一个往旧、一个往新
/// </summary>
internal sealed class ConversationLatestNavigator
{
    private const double ButtonThreshold = 160.0; //离底多远才亮箭头:流式增长在补底之前的那一瞬也算离底,不该跟着闪
    private const double LoadLaterThreshold = 32.0; //离底多少像素以内算滚到底,留余量让续批在撞底之前开始

    private readonly ScrollViewer _viewer;
    private readonly Control _button;
    private readonly ScrollViewerAutoScrollHolder _autoScroll;
    private readonly Action _scrollToBottom;
    private readonly Action _onItemsChanged;
    private ConversationViewModel? _viewModel;
    private bool _isLoadingLater; //正在往后续一批(防抖)

    /// <summary>构造</summary>
    /// <param name="viewer">消息流滚动容器</param>
    /// <param name="button">「回到最新」箭头</param>
    /// <param name="autoScroll">跟底</param>
    /// <param name="scrollToBottom">贴到底</param>
    /// <param name="onItemsChanged">条目或视口变了之后的收尾（清扫视口里的卡）</param>
    public ConversationLatestNavigator(ScrollViewer viewer, Control button, ScrollViewerAutoScrollHolder autoScroll,
        Action scrollToBottom, Action onItemsChanged)
    {
        _viewer = viewer;
        _button = button;
        _autoScroll = autoScroll;
        _scrollToBottom = scrollToBottom;
        _onItemsChanged = onItemsChanged;
    }

    private double DistanceFromBottom => _viewer.Extent.Height - _viewer.Viewport.Height - _viewer.Offset.Y;

    /// <summary>换了视图模型。跟底状态是全局唯一那一份，切回一个停在旧消息那段的缓存实例时要跟着停</summary>
    /// <param name="viewModel">新的视图模型；解绑为 null</param>
    public void Bind(ConversationViewModel? viewModel)
    {
        _viewModel = viewModel;
        if (viewModel == null) return;

        _autoScroll.IsSuspended = viewModel.HasLaterMessages;
        UpdateButton();
    }

    /// <summary>
    /// 脱离末尾的状态翻了。脱离时「底部」不是最新，跟过去只会让滚到底续一批连环触发；
    /// 自然接回末尾（往下续到头、重试截到这里）时此刻就在底部，恢复跟底，新一轮才跟得上
    /// </summary>
    public void OnDetachedChanged()
    {
        if (_viewModel == null) return;

        _autoScroll.IsSuspended = _viewModel.HasLaterMessages;
        if (!_viewModel.HasLaterMessages) _autoScroll.Resume();
        UpdateButton();
    }

    /// <summary>刷新箭头显隐：往上翻离底一段、或脱离末尾时显示</summary>
    public void UpdateButton() =>
        _button.IsVisible = _viewModel is { HasLaterMessages: true } || DistanceFromBottom > ButtonThreshold;

    /// <summary>
    /// 脱离末尾时滚到底，往后续一批。追加在下面，视口不用补偿；续到历史末尾就重新跟上末尾
    /// </summary>
    public void TryLoadLater()
    {
        if (_isLoadingLater || _viewModel is not { HasLaterMessages: true } vm) return;
        if (DistanceFromBottom > LoadLaterThreshold) return;

        _isLoadingLater = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (vm.LoadLaterMessages()) _onItemsChanged();
            Dispatcher.UIThread.Post(() => _isLoadingLater = false, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 点箭头。脱离末尾时末尾没画，重放最新（落位由视图模型的 ReturnedToLatest 那一路做）；只是往上翻了就直接贴底
    /// </summary>
    public void JumpToLatest()
    {
        if (_viewModel is not { } vm) return;

        if (vm.HasLaterMessages)
        {
            vm.ReturnToLatestCommand.Execute(null);
            return;
        }

        _scrollToBottom();
        _autoScroll.Resume();
        _onItemsChanged();
    }
}
