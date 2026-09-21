/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.VisualTree;
using UiharuMind.Shared.Controls;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话流的视口带：滚远一屏以外的<b>整张卡片</b>收成等高空占位，视口内的当场装回来并转出 markdown
/// （见 ADR 0041）。
///
/// <para><b>为什么收整张卡而不只收正文。</b>正文那棵树只占一张卡的一半——实测 60 条的会话，
/// 只卸正文能把整表从 3663 个可视对象压到 2343，剩下的 2343 全是卡片外壳
/// （头像、时间戳、气泡边框、操作行，每张约 39 个）。而外壳这一半对每种卡片都得单独接一遍入口，
/// 收整张卡则一次覆盖文本卡/工具卡/思考卡/审批卡，还省掉了「气泡自己维护一套卸载状态机」
/// 那份复杂度（那套状态机正是「用户消息滚回来变空白」那个缺陷的来源）。</para>
///
/// <para><b>等高仍是唯一的验收口径。</b>容器（<see cref="ContentPresenter"/>）在竖排里横向本来就拉满，
/// 所以只有高度需要钉住；钉的是<b>刚刚量到的</b>那个高度，Extent 因此一分不动，
/// 跟底、滚到顶续窗、前插补偿三条路径一行都不用改。</para>
///
/// <para><b>按容器走，不翻整棵树。</b>会话流没有虚拟化，整表可以有几千个可视对象，
/// 而一次滚动会清扫很多次。容器只有条目数那么多；容器里那个气泡认准一次就记下来。</para>
/// </summary>
internal sealed class ConversationCardViewport
{
    /// <summary>
    /// 上下各留几屏缓冲再动手。贴着视口边缘卸载，轻微滚动就会来回拆建，而重建是要钱的
    /// </summary>
    private const double BufferScreens = 1.0;

    private readonly ScrollViewer _viewer;
    private readonly ItemsControl _list;
    private readonly Dictionary<Control, SimpleMarkdownViewer?> _bubbles = new(); //容器 -> 它那个气泡(非文本卡记 null)
    private readonly Dictionary<ContentPresenter, object> _unloaded = new(); //占位中的容器 -> 它原来的条目

    /// <param name="viewer">会话流的滚动容器</param>
    /// <param name="list">消息列表</param>
    public ConversationCardViewport(ScrollViewer viewer, ItemsControl list)
    {
        _viewer = viewer;
        _list = list;
        _list.ContainerClearing += OnContainerClearing;
    }

    /// <summary>
    /// 清扫一遍：滚远的卡片收成占位，视口内的装回来并把 markdown 转出来。
    /// 没有前半件的话，一窗卡片滚过一遍就全部实化在那里不走了
    /// </summary>
    public void Sweep()
    {
        double viewportHeight = _viewer.Viewport.Height;
        if (viewportHeight <= 0) return;

        double far = viewportHeight * BufferScreens;
        bool reloaded = false;
        foreach (ContentPresenter container in Containers())
        {
            if (!TryGetBand(container, out double top, out double bottom)) continue;

            if (bottom < -far || top > viewportHeight + far) Unload(container);
            else if (bottom >= 0 && top <= viewportHeight) reloaded |= Reload(container);
        }

        // 刚装回来的卡片此刻只是个 Content,视觉树要等一次布局才长出来。先让它落地,
        // 紧接着把视口里的 markdown 当场转掉——否则用户会先看到一帧原文再变 markdown
        if (reloaded) _viewer.UpdateLayout();
        RealizeVisible();
    }

    /// <summary>
    /// 把此刻落在视口内、还没转 markdown 的气泡当场转掉。
    ///
    /// 不走 <c>SimpleMarkdownViewer</c> 的排队机制：那个队列靠视口通知填充，而视口通知的
    /// 处理器是在气泡 <c>OnLoaded</c> 时订阅的——列表刚建出来时气泡还没 <c>Loaded</c>，
    /// 队列因此是空的，这一屏就会退回逐帧放行，表现为先显示原文再变 markdown。
    /// 这里直接按几何判断，不依赖任何事件时序。
    /// </summary>
    /// <returns>真的转了至少一个返回 true</returns>
    public bool RealizeVisible()
    {
        double viewportHeight = _viewer.Viewport.Height;
        if (viewportHeight <= 0) return false;

        bool realizedAny = false;
        foreach (ContentPresenter container in Containers())
        {
            if (!TryGetBand(container, out double top, out double bottom)) continue;
            if (bottom < 0 || top > viewportHeight) continue;

            SimpleMarkdownViewer? bubble = ResolveBubble(container);
            if (bubble != null && bubble.RealizeNow()) realizedAny = true;
        }

        return realizedAny;
    }

    /// <summary>
    /// 卡片整棵丢掉，只留一个等高空占位。
    /// <c>Content = null</c> 之后 <see cref="ContentPresenter"/> 会在下一次布局里把子树摘掉并丢弃
    /// </summary>
    private void Unload(ContentPresenter container)
    {
        if (_unloaded.ContainsKey(container)) return;

        object? item = container.Content;
        if (item == null) return;

        double height = container.Bounds.Height;
        if (height <= 0) return; //没量到几何,拆了就没法等高占位

        _unloaded[container] = item;
        container.Height = height;
        container.Content = null;
    }

    /// 装回卡片：模板重新实例化一份。返回是否真的装回了（调用方据此决定要不要补一次布局）
    private bool Reload(ContentPresenter container)
    {
        if (!_unloaded.Remove(container, out object? item)) return false;

        container.Content = item;
        container.ClearValue(Layoutable.HeightProperty); //占位高度让位给真实高度
        return true;
    }

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        _bubbles.Remove(e.Container);
        if (e.Container is ContentPresenter presenter) _unloaded.Remove(presenter);
    }

    /// 容器相对 <c>Viewer</c> 的上下沿。这一步换算已经把滚动偏移算进去了
    private bool TryGetBand(Visual target, out double top, out double bottom)
    {
        top = 0;
        bottom = 0;
        Point? topLeft = target.TranslatePoint(default, _viewer);
        if (topLeft == null) return false;

        top = topLeft.Value.Y;
        bottom = top + target.Bounds.Height;
        return true;
    }

    private IEnumerable<ContentPresenter> Containers()
    {
        foreach (Control? container in _list.GetRealizedContainers())
        {
            if (container is ContentPresenter presenter) yield return presenter;
        }
    }

    /// <summary>
    /// 容器里那个气泡，认过一次就记住。工具卡/思考卡这类没有气泡的记成 null，
    /// 一样不再往里翻——它们恰恰是最深的那几棵树。
    ///
    /// 记下来的实例掉出视觉树（卡片重建过）就重认；模板还没长出来则这一轮先当它没有，
    /// 不把一个过早的 null 记死
    /// </summary>
    private SimpleMarkdownViewer? ResolveBubble(Control container)
    {
        if (_bubbles.TryGetValue(container, out SimpleMarkdownViewer? cached) &&
            (cached == null || cached.IsAttachedToVisualTree()))
        {
            return cached;
        }

        if (container.GetVisualChildren().FirstOrDefault() == null) return null;

        SimpleMarkdownViewer? bubble = container.GetVisualDescendants().OfType<SimpleMarkdownViewer>().FirstOrDefault();
        _bubbles[container] = bubble;
        return bubble;
    }
}
