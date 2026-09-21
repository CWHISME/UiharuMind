using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls.TextRendering;

/// <summary>
/// 可选中文本块 + <b>自绘背景装饰段</b>：<see cref="TextBackgroundSpan"/> 声明字符区间与背景，
/// 渲染期用 <c>TextLayout</c> 反查每段与每条视觉行的交集矩形后绘制。
///
/// 为什么自绘而不是 <c>Run.Background</c>：<c>Run.Background</c> 只画 <c>GlyphRun.Bounds</c>
/// （字形框），整行背景、行间无缝、padding/圆角都做不到（见 Avalonia ShapedTextRun.Draw）。
/// 按<b>字符跨度</b>反查矩形（行遍历求交集，取自 LiveMarkdown.Avalonia 的
/// GetCodeInlineSpanRects，MIT）天然正确处理空行/换行——不会像「行索引 ↔ TextLine 索引」
/// 那样错位。
///
/// 应用顺序：装饰背景 →（base）选中高亮 → 文字。选中区盖在背景之上，文字永远最上层。
/// </summary>
public class DecoratedTextBlock : SelectableTextBlock
{
    /// <summary>背景装饰段；null / 空集合时不画任何背景</summary>
    public static readonly StyledProperty<IReadOnlyList<TextBackgroundSpan>?> DecorationsProperty =
        AvaloniaProperty.Register<DecoratedTextBlock, IReadOnlyList<TextBackgroundSpan>?>(nameof(Decorations));

    static DecoratedTextBlock()
    {
        DecorationsProperty.Changed.AddClassHandler<DecoratedTextBlock>(static (o, _) => o.InvalidateVisual());
    }

    /// <summary>样式仍按 <see cref="SelectableTextBlock"/> 命中（mono/muted 类样式、选中高亮与复制菜单的主题都原样适用）</summary>
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    /// <summary>
    /// 背景装饰段；null / 空集合时不画任何背景
    /// </summary>
    public IReadOnlyList<TextBackgroundSpan>? Decorations
    {
        get => GetValue(DecorationsProperty);
        set => SetValue(DecorationsProperty, value);
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        DrawDecorations(context, origin);
        base.RenderTextLayout(context, origin);
    }

    private void DrawDecorations(DrawingContext context, Point origin)
    {
        var decorations = Decorations;
        if (decorations == null || decorations.Count == 0) return;

        var lines = TextLayout.TextLines;
        if (lines.Count == 0) return;

        // 与 TextLayout.Draw 同一套行定位：origin 起逐行累加 Height
        double y = origin.Y;
        foreach (var line in lines)
        {
            int lineStart = line.FirstTextSourceIndex;
            foreach (var decoration in decorations)
            {
                int start = Math.Max(decoration.Start, lineStart);
                int end = Math.Min(decoration.End, lineStart + line.Length);
                if (start >= end || decoration.Background == null) continue;

                if (decoration.FillRowWidth)
                {
                    // diff 场景：从控件左边缘铺满整宽（含 Padding，与旧 TextBlock.Background
                    // 覆盖整个控件一致；行几何由交集保证 y/height 正确）。
                    // 像素取整（与选中高亮同款）：行高常带小数，不取整相邻块的抗锯齿边缘
                    // 会合成出一条细缝
                    var rowRect = PixelRect.FromRect(
                        new Rect(0, y, Bounds.Width, line.Height), 1).ToRect(1);
                    context.FillRectangle(decoration.Background, rowRect);
                    continue;
                }

                // 精确文字矩形（可跨行→多个矩形）+ padding + 圆角
                foreach (var bounds in line.GetTextBounds(start, end - start))
                {
                    var r = bounds.Rectangle.Translate(new Vector(0, y));
                    var rect = new Rect(
                        r.X - decoration.Padding.Left,
                        r.Y - decoration.Padding.Top,
                        r.Width + decoration.Padding.Left + decoration.Padding.Right,
                        r.Height + decoration.Padding.Top + decoration.Padding.Bottom);
                    var pixelRect = PixelRect.FromRect(rect, 1).ToRect(1);
                    if (decoration.Radius > 0)
                    {
                        context.FillRectangle(decoration.Background, pixelRect, (float)decoration.Radius);
                    }
                    else
                    {
                        context.FillRectangle(decoration.Background, pixelRect);
                    }
                }
            }

            y += line.Height;
        }
    }
}