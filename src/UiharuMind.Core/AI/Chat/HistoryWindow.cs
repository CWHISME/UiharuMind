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
    private int _firstScreenDeficit; //首屏之后还欠多少条才够整窗

    /// <summary>默认每批窗口大小</summary>
    public const int DefaultSize = 10;

    /// <summary>
    /// 默认首屏批次大小。切会话时先只渲染这么多条，剩下的凑够一窗由
    /// <see cref="FillFirstWindow"/> 在界面已经可见之后补上——
    /// 非虚拟化列表的开销压倒性地在布局上（实测一窗 20 条里，四次布局占 274ms 中的 231ms），
    /// 而首屏要的只是「填满一屏」，不是「凑够一窗」。
    /// </summary>
    public const int DefaultFirstScreenSize = 5;

    /// <summary>每批窗口大小</summary>
    public int Size { get; }

    /// <summary>首屏批次大小</summary>
    public int FirstScreenSize { get; }

    /// <summary>当前窗口在完整历史中的起点</summary>
    public int Start { get; private set; }

    /// <summary>起点之前是否还有更早的消息</summary>
    public bool HasEarlier => Start > 0;

    /// <param name="size">每批窗口大小，非正值按默认处理</param>
    /// <param name="firstScreenSize">首屏批次大小，非正值按默认处理；不小于 <paramref name="size"/> 时等于关掉分批</param>
    public HistoryWindow(int size = DefaultSize, int firstScreenSize = DefaultFirstScreenSize)
    {
        Size = size > 0 ? size : DefaultSize;
        FirstScreenSize = Math.Min(Size, firstScreenSize > 0 ? firstScreenSize : DefaultFirstScreenSize);
    }

    /// <summary>
    /// 重置到历史尾部的<b>首屏</b>（切换会话或首次回放）。
    /// 只给 <see cref="FirstScreenSize"/> 条，凑够整窗的那一段记在账上，
    /// 由 <see cref="FillFirstWindow"/> 稍后补。
    /// </summary>
    /// <param name="messageCount">完整历史的消息条数</param>
    /// <returns>要渲染的区间 [From, To)</returns>
    public (int From, int To) Reset(int messageCount)
    {
        int count = Math.Max(0, messageCount);
        Start = Math.Max(0, count - FirstScreenSize);
        _firstScreenDeficit = Math.Min(Size, count) - (count - Start);
        return (Start, count);
    }

    /// <summary>
    /// 把首屏补齐到整窗。与 <see cref="Extend"/> 的区别只在批量：
    /// 补的是 <see cref="Reset"/> 时刻意没给的那几条，一次性还清，此后不再欠。
    /// </summary>
    /// <param name="messageCount">完整历史的消息条数</param>
    /// <returns>要前插的区间 [From, To)；不欠或已到历史开头时为 null</returns>
    public (int From, int To)? FillFirstWindow(int messageCount)
    {
        if (_firstScreenDeficit <= 0) return null;

        int deficit = _firstScreenDeficit;
        _firstScreenDeficit = 0;

        int end = Math.Min(Start, Math.Max(0, messageCount));
        if (end <= 0)
        {
            Start = 0;
            return null;
        }

        int from = Math.Max(0, end - deficit);
        Start = from;
        return (from, end);
    }

    /// <summary>
    /// 向前扩展一窗
    /// </summary>
    /// <param name="messageCount">完整历史的消息条数</param>
    /// <returns>要前插的区间 [From, To)；已到历史开头时为 null</returns>
    public (int From, int To)? Extend(int messageCount)
    {
        _firstScreenDeficit = 0; //用户已经自己往前翻,首屏那笔账作废
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
    /// 直接把窗口起点挪到指定下标（运行期裁剪用）。
    ///
    /// 与 <see cref="Reset"/> / <see cref="Extend"/> 的区别是<b>起点由外部锚点给定</b>：
    /// 裁剪方是按界面条目找到边界的，那条边界对应哪个历史下标只有它知道，
    /// 本类算不出来。窗口仍然只负责回答「起点在哪」，语义没有变宽。
    /// </summary>
    /// <param name="start">新的窗口起点；负值按 0 处理</param>
    public void SetStart(int start)
    {
        // 裁剪是把起点往后挪,此时再补首屏就是把刚裁掉的一段又贴回去
        _firstScreenDeficit = 0;
        Start = Math.Max(0, start);
    }

    /// <summary>
    /// 清空（回到无历史状态）
    /// </summary>
    public void Clear()
    {
        _firstScreenDeficit = 0;
        Start = 0;
    }
}
