/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Globalization;
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

    private LineNumbering _numbering = LineNumbering.Whole; //只读片段（Read 从第 60 行读起、末尾带附注）要对上原文件行号

    /// <summary>行号口径，默认从 1 起全部编号</summary>
    public LineNumbering LineNumbering
    {
        get => _numbering;
        set
        {
            if (_numbering == value) return;
            _numbering = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double fontSize = GetValue(TextBlock.FontSizeProperty);
        if (double.IsNaN(fontSize) || fontSize <= 0)
            SetValue(TextBlock.FontSizeProperty, FallbackFontSize);
        try
        {
            Size size = base.MeasureOverride(availableSize);
            return _numbering.IsWhole ? size : new Size(MeasureNumber(LastLineNumber()).Width, size.Height);
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
            if (_numbering.IsWhole) base.Render(drawingContext);
            else RenderShifted(drawingContext);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 跳过这一帧，下次 Measure 回填正确值。
        }
    }

    private int LastLineNumber() => _numbering.FirstLine + Math.Max(1, Document?.LineCount ?? 1) - 1;

    private FormattedText MeasureNumber(int number) =>
        new(number.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface, EmSize, GetValue(TextBlock.ForegroundProperty));

    // 复刻上游 Render，行号加上偏移、附注行不编号
    private void RenderShifted(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView is not { VisualLinesValid: true }) return;
        double width = Bounds.Width;
        foreach (var line in textView.VisualLines)
        {
            if (_numbering.NumberOf(line.FirstDocumentLine.LineNumber) is not { } number) continue;
            FormattedText text = MeasureNumber(number);
            double y = line.GetTextLineVisualYPosition(line.TextLines[0], AvaloniaEdit.Rendering.VisualYPosition.TextTop);
            drawingContext.DrawText(text, new Point(width - text.Width, y - textView.VerticalOffset));
        }
    }
}
