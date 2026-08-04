/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 历史渲染窗口：对外只说「这次该渲染哪个下标区间」，把开窗与前扩的下标运算收在内部。
/// 会话列表不做虚拟化，靠数据开窗保住长会话的渲染性能。
/// </summary>
public sealed class HistoryWindow
{
    /// <summary>默认每批窗口大小</summary>
    public const int DefaultSize = 20;

    /// <summary>每批窗口大小</summary>
    public int Size { get; }

    /// <summary>当前窗口在完整历史中的起点</summary>
    public int Start { get; private set; }

    /// <summary>起点之前是否还有更早的消息</summary>
    public bool HasEarlier => Start > 0;

    /// <param name="size">每批窗口大小，非正值按默认处理</param>
    public HistoryWindow(int size = DefaultSize)
    {
        Size = size > 0 ? size : DefaultSize;
    }

    /// <summary>
    /// 重置到历史尾部一窗（切换会话或首次回放）
    /// </summary>
    /// <param name="messageCount">完整历史的消息条数</param>
    /// <returns>要渲染的区间 [From, To)</returns>
    public (int From, int To) Reset(int messageCount)
    {
        int count = Math.Max(0, messageCount);
        Start = Math.Max(0, count - Size);
        return (Start, count);
    }

    /// <summary>
    /// 向前扩展一窗
    /// </summary>
    /// <param name="messageCount">完整历史的消息条数</param>
    /// <returns>要前插的区间 [From, To)；已到历史开头时为 null</returns>
    public (int From, int To)? Extend(int messageCount)
    {
        int end = Math.Min(Start, Math.Max(0, messageCount));
        if (end <= 0)
        {
            Start = 0;
            return null;
        }

        int from = Math.Max(0, end - Size);
        Start = from;
        return (from, end);
    }

    /// <summary>
    /// 清空（回到无历史状态）
    /// </summary>
    public void Clear()
    {
        Start = 0;
    }
}
