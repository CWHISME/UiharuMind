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
using AvaloniaEdit.Editing;
using AvaloniaEdit.Utils;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 上游 <see cref="LineNumberMargin"/> 的 EmSize/TypeFace 字段只在 MeasureOverride 里赋值，
/// 首次布局前若先 Render，建 FormattedText 拿到 emSize=0 直接抛，
/// 异常进渲染管线会带崩整个应用。非法时用字号回填，仍非法则跳过这一帧
/// （只丢行号，不崩应用；下次 Measure 会回填正确值）。
/// </summary>
public sealed class SafeLineNumberMargin : LineNumberMargin
{
    private const double FallbackFontSize = 13;

    protected override Size MeasureOverride(Size availableSize)
    {
        double fontSize = GetValue(TextBlock.FontSizeProperty);
        if (double.IsNaN(fontSize) || fontSize <= 0)
            SetValue(TextBlock.FontSizeProperty, FallbackFontSize);
        try
        {
            return base.MeasureOverride(availableSize);
        }
        catch (ArgumentOutOfRangeException)
        {
            EmSize = FallbackFontSize;
            Typeface = this.CreateTypeface();
            return new Size(0, 0);
        }
    }

    public override void Render(DrawingContext drawingContext)
    {
        if (double.IsNaN(EmSize) || EmSize <= 0)
        {
            double fontSize = GetValue(TextBlock.FontSizeProperty);
            EmSize = double.IsNaN(fontSize) || fontSize <= 0 ? FallbackFontSize : fontSize;
            Typeface = this.CreateTypeface();
        }
        try
        {
            base.Render(drawingContext);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 跳过这一帧，下次 Measure 回填正确值。
        }
    }
}
