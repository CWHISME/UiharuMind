/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia.Threading;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// <b>观察别人驱动的那一轮</b>时的渲染落点：把内容搬到 UI 线程，并挡掉不归观察者管的那些。
///
/// 跨线程这一层必须有：子代理与定时任务都跑在后台线程上，而落点最终写的是界面绑定集合
/// ——Avalonia 的弱事件簿记不是线程安全的，跨线程写会在 <c>WeakHashList</c> 内部抛
/// <c>NullReferenceException</c>（已实际撞到过）。一律 <c>Post</c>，不走「已在 UI 线程就直接调」
/// 的捷径：那样同一串内容会一部分排队一部分插队，顺序就乱了。
/// </summary>
public sealed class LiveObserverSink : ITurnSink
{
    private readonly ITurnSink _inner;

    /// <param name="inner">真正的渲染落点（转录器）</param>
    public LiveObserverSink(ITurnSink inner) => _inner = inner;

    /// <inheritdoc />
    public void Apply(AIContent content)
    {
        // 审批请求要送到<b>派活者那一轮</b>的回应口(见 ADR 0021):这个窗口画出来也按不动,
        // 而且永远等不到回应,会一直挂在待决清单上
        if (content is ToolApprovalRequestContent) return;

        Post(() => _inner.Apply(content));
    }

    /// <inheritdoc />
    public void CloseSegment() => Post(() => _inner.CloseSegment());

    /// <inheritdoc />
    public void StopRunningToolCalls(string note) => Post(() => _inner.StopRunningToolCalls(note));

    /// <summary>
    /// 观察者的正文不进任何人的历史，所以只做它的另一半职责——收尾当前流段——并返回 null。
    /// 返回值得同步给出，而这里的调用来自后台线程，取不到也不该取。
    /// </summary>
    /// <returns>恒为 null</returns>
    public string? TakeStreamingText()
    {
        CloseSegment();
        return null;
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
