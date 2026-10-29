/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 提示浮层里的长文案（多行、自动换行）。
///
/// <b>为什么不是裸 <see cref="TextBlock"/></b>：提示框的宽度是按「文字自然宽度 + 内边距」定的，
/// 于是排版时拿到的那点宽度刚好等于文字的自然宽度，再被内边距/像素取整磨掉零点几像素——
/// 最后几个字就换到下一行去了。而高度是按测量时那份（还宽一点点）算的，
/// 多出来的这一行没有地方画：<see cref="TextBlock"/> 在 <c>ArrangeOverride</c> 里是丢掉旧排版、
/// 按拿到的尺寸重建一份，又 <c>ClipToBounds</c>，于是整行消失，还没有省略号。
/// 单行的 tip（「167.3k / 1M」）会被拆成两行只显示第一行；长 tip 会少最后一句。
/// 以前靠 <c>TextWrapping="NoWrap"</c> 绕，代价是一长就横向截断。
///
/// 这里把排版尺寸取「测量时那份」与「现在给的」中较大的一个：宽度回到测量时的可用宽度，
/// 就不会重新折行；高度也不低于自己要的高度，那一行不会再被丢掉。多出来的部分落在提示框
/// 自己的内边距里，看不出来；短文案也不会像给死宽度那样留一大块空。
///
/// 不要改成给死宽度（<c>Width</c>）：短文案会在提示框里留一大块空。
/// </summary>
public class TipText : TextBlock
{
    private Size _measureConstraint; // 测量时用到的约束，排版要按同一份来

    /// <summary>
    /// 建一个「多行、自动换行、按测量时的宽度和高度排版」的提示文案
    /// </summary>
    public TipText()
    {
        // 不靠继承进来的样式：提示内容的逻辑父级是挂 Tip 的那个控件，单行 + 省略号的设置会跟着下来
        TextWrapping = TextWrapping.Wrap;
        TextTrimming = TextTrimming.None;
        MaxLines = 0;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // 这一份就是 TextLayout 建出来时用的宽度；排版时必须沿用它，否则换行位置会变
        _measureConstraint = availableSize;
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = double.IsInfinity(_measureConstraint.Width)
            ? finalSize.Width
            : Math.Max(finalSize.Width, _measureConstraint.Width);
        double height = Math.Max(finalSize.Height, DesiredSize.Height);

        return base.ArrangeOverride(new Size(width, height));
    }
}
