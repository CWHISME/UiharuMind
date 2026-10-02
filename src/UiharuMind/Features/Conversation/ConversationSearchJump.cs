/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Search;
using UiharuMind.Shared.Controls;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 搜索跳转的视图那一半：让命中那条进窗、滚到视口上部、闪一下（见 ADR 0056）。
///
/// 进窗优先<b>截断重载</b>（视图模型只围着命中画一小段），整件事在一个派发任务里做完，中间不出帧；
/// 有轮在跑时截不得，才退回分批续窗：走「滚到顶自动续」同一条前插补偿路径，批间让出 UI 线程，
/// 代价是跳得远时一批批前插看得见
/// </summary>
internal sealed class ConversationSearchJump
{
    private const double ViewportAnchor = 0.25; //命中落在视口上方四分之一处:上文看得到一点,下文留得多
    private const double InsetGap = 12; //落点与搜索栏下沿之间留的空
    private const int MaxSettlePasses = 6; //转 markdown 会改高度,落点要跟着重算;垫高续批也占一轮
    private const int MaxLaterTopUps = 3; //命中之后的内容不够高、滚不到锚点时,往后续几批垫高

    private readonly ScrollViewer _viewer;
    private readonly ItemsControl _list;
    private readonly ConversationCardViewport _cards;
    private readonly ScrollViewerAutoScrollHolder _autoScroll;
    private readonly Func<Func<bool>, bool> _prependKeepingViewport;
    private readonly Func<double> _topInset;
    private readonly Func<bool> _loadLater;
    private readonly ConversationSearchHighlight _highlight = new();
    private int _version; //后一次跳转作废前一次还在续窗的那次

    /// <summary>构造</summary>
    /// <param name="viewer">会话流的滚动容器</param>
    /// <param name="list">消息列表</param>
    /// <param name="cards">卡片视口带</param>
    /// <param name="autoScroll">跟底状态</param>
    /// <param name="prependKeepingViewport">前插并补偿视口（与滚到顶续窗共用的那一份）</param>
    /// <param name="topInset">视口顶部被浮层（搜索栏）盖住的高度，落点要让到它下面</param>
    /// <param name="loadLater">脱离末尾时往后续一批（命中之后不够高、滚不到锚点时垫高）</param>
    public ConversationSearchJump(ScrollViewer viewer, ItemsControl list, ConversationCardViewport cards,
        ScrollViewerAutoScrollHolder autoScroll, Func<Func<bool>, bool> prependKeepingViewport, Func<double> topInset,
        Func<bool> loadLater)
    {
        _topInset = topInset;
        _loadLater = loadLater;
        _viewer = viewer;
        _list = list;
        _cards = cards;
        _autoScroll = autoScroll;
        _prependKeepingViewport = prependKeepingViewport;
    }

    /// <summary>跳到一条命中。<b>必须在 UI 线程上调用</b></summary>
    /// <param name="navigator">当前会话的跳转决策（跳转期间换了会话就作罢）</param>
    /// <param name="hit">命中</param>
    /// <param name="isCurrent">这个决策对象还是不是当前会话那个</param>
    public async Task JumpAsync(ConversationSearchNavigator navigator, SessionSearchHit hit,
        Func<ConversationSearchNavigator, bool> isCurrent)
    {
        int version = ++_version;
        long started = Stopwatch.GetTimestamp();
        int batches = 0;
        // 先让出跟底:不让的话流式增长与运行期裁剪会把视口拽回底部、把刚续出来的那段收回去
        _autoScroll.Release();

        ESearchJumpPlan plan = navigator.Plan(hit);
        if (plan == ESearchJumpPlan.NotFound) return;
        while (plan == ESearchJumpPlan.Incremental && _prependKeepingViewport(() => navigator.LoadEarlierToward(hit)))
        {
            batches++;
            _cards.Sweep();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (version != _version || !isCurrent(navigator)) return;
        }

        if (navigator.Reveal(hit) is not { } item) return;
        if (_list.ContainerFromItem(item) is not Control container) return;

        // 展开折叠卡、视口里的卡转 markdown 都会改高度(清扫会顺带把视口里的转掉),落点跟着重算到稳定为止——
        // 重载之后视口里全是原文,不当场转完会先画一帧原文再变
        int topUps = 0;
        for (int pass = 0; pass < MaxSettlePasses; pass++)
        {
            _viewer.UpdateLayout();
            if (ScrollTo(container) is not { } clamped) return;
            // 截断重载只在命中之后画了一小段,短消息凑不满一屏时落点会被滚动下限卡在锚点下方(被搜索栏盖住)
            if (clamped && topUps < MaxLaterTopUps && _loadLater())
            {
                topUps++;
                continue;
            }

            _viewer.UpdateLayout();
            _cards.Sweep(); //落点那一屏的卡可能是占位,当场装回来并转出 markdown
            _viewer.UpdateLayout();
            if (container.TranslatePoint(default, _viewer) is not { } settled ||
                Math.Abs(settled.Y - Anchor()) < 1 || IsAtScrollLimit()) break;
        }

        _highlight.Show(container);
        Log.Debug($"Search jump: {plan}, {batches} batch(es), " +
                  $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}ms, items {_list.ItemCount}");
    }

    /// <summary>滚到锚点</summary>
    /// <returns>被滚动下限卡住(内容不够高)为 true;量不到几何为 null</returns>
    private bool? ScrollTo(Control container)
    {
        if (container.TranslatePoint(default, _viewer) is not { } top) return null;

        double max = Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height);
        double target = _viewer.Offset.Y + top.Y - Anchor();
        _viewer.Offset = new Vector(_viewer.Offset.X, Math.Clamp(target, 0, max));
        return target > max + 1;
    }

    // 落点离视口顶多远:视口上方四分之一,搜索栏盖得更深就让到它下沿以下
    private double Anchor() => Math.Max(_viewer.Viewport.Height * ViewportAnchor, _topInset() + InsetGap);

    private bool IsAtScrollLimit()
    {
        double max = Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height);
        return _viewer.Offset.Y <= 0 || _viewer.Offset.Y >= max - 1;
    }
}
