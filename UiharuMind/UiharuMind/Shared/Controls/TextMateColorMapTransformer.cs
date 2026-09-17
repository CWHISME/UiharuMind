/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using TextMateSharp.Themes;
using FontStyle = TextMateSharp.Themes.FontStyle;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 查表型语法高亮 transformer：颜色由 <see cref="TextMatePreTokenizer"/> 预计算好后塞进来，
/// 渲染（<see cref="ColorizeLine"/>）时只查表，不做任何 TextMate 正则匹配。
///
/// 为什么要绕开 <c>AvaloniaEdit.TextMate</c> 的官方集成：它的 <c>TextMateColoringTransformer</c>
/// 在渲染每个可视行时同步调 <c>model.GetLineTokens</c>，而拖选长行越过视口右边缘会触发
/// AvaloniaEdit <c>SelectionMouseHandler</c> 的无限事件循环（上游 issue #606，12.0.0 未修）；
/// 循环每轮迭代都在重新上色，官方集成把 tokenize 成本叠进循环里，UI 线程直接被填满冻死。
/// 本类把成本从「循环内」挪到「循环外」（装载后分帧预 tokenize），循环内只剩查表，
/// 实测拖选路径与无高亮同量级（毫秒级完成）。
/// </summary>
public sealed class TextMateColorMapTransformer : DocumentColorizingTransformer
{
    /// <summary>一行上的一个着色段（位置 + 主题匹配结果）</summary>
    public sealed class ColorSpan
    {
        /// <summary>行内字符偏移（0 起）</summary>
        public int Start;

        /// <summary>段长（字符数）</summary>
        public int Length;

        /// <summary>token 的 scopes。主题切换时用它重新匹配颜色，不重新 tokenize</summary>
        public List<string>? Scopes;

        /// <summary>主题匹配后的颜色 id（&lt;=0 表示无）</summary>
        public int Foreground;

        /// <summary>主题匹配后的背景色 id（&lt;=0 表示无）</summary>
        public int Background;

        /// <summary>TextMateSharp 的 <see cref="FontStyle"/> 位标志（-1/0 表示无）</summary>
        public int FontStyle;
    }

    private readonly Dictionary<int, ColorSpan[]> _lineSpans = new();
    private readonly Dictionary<int, IBrush> _brushCache = new();
    private Theme? _theme;

    /// <summary>换主题：保留 scopes，只重算颜色并清 brush 缓存（调用方随后负责重绘）</summary>
    public void SetTheme(Theme theme)
    {
        _theme = theme;
        _brushCache.Clear();
        if (_theme == null) return;

        foreach (var spans in _lineSpans.Values)
        {
            foreach (var span in spans)
            {
                if (span.Scopes == null || span.Scopes.Count == 0) continue;
                MatchColor(span);
            }
        }
    }

    /// <summary>整表替换（换文档/重新全量 tokenize 后）</summary>
    public void ReplaceAllLines(Dictionary<int, ColorSpan[]> spans)
    {
        _lineSpans.Clear();
        foreach (var pair in spans) _lineSpans[pair.Key] = pair.Value;
        if (_theme != null)
        {
            foreach (var pair in _lineSpans)
            {
                foreach (var span in pair.Value)
                {
                    if (span.Scopes == null || span.Scopes.Count == 0) continue;
                    MatchColor(span);
                }
            }
        }
    }

    /// <summary>从某行（含）起清掉（编辑后重跑区间用）</summary>
    /// <param name="lineNumber">1 起算的行号</param>
    public void ClearFrom(int lineNumber)
    {
        var keys = new List<int>();
        foreach (var key in _lineSpans.Keys)
        {
            if (key >= lineNumber) keys.Add(key);
        }
        foreach (var key in keys) _lineSpans.Remove(key);
    }

    /// <summary>写入单行段列表</summary>
    public void SetLine(int lineNumber, ColorSpan[] spans)
    {
        if (_theme != null)
        {
            foreach (var span in spans)
            {
                if (span.Scopes == null || span.Scopes.Count == 0) continue;
                MatchColor(span);
            }
        }
        _lineSpans[lineNumber] = spans;
    }

    public void ClearAll()
    {
        _lineSpans.Clear();
        _brushCache.Clear();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_theme == null) return;
        if (!_lineSpans.TryGetValue(line.LineNumber, out var spans)) return;

        foreach (var span in spans)
        {
            if (span.Length <= 0) continue;
            if (span.Foreground <= 0 && span.Background <= 0 && span.FontStyle <= 0) continue;

            ChangeLinePart(
                line.Offset + span.Start,
                line.Offset + span.Start + span.Length,
                element =>
                {
                    if (span.Foreground > 0 && GetBrush(span.Foreground) is { } fg)
                        element.TextRunProperties.SetForegroundBrush(fg);

                    if (span.Background > 0 && GetBrush(span.Background) is { } bg)
                        element.BackgroundBrush = bg;

                    if (span.FontStyle > 0)
                        ApplyFontStyle(element, span.FontStyle);
                });
        }
    }

    private void MatchColor(ColorSpan span)
    {
        if (_theme == null || span.Scopes == null) return;
        span.Foreground = 0;
        span.Background = 0;
        span.FontStyle = 0;

        foreach (var rule in _theme.Match(span.Scopes))
        {
            if (span.Foreground == 0 && rule.foreground > 0) span.Foreground = rule.foreground;
            if (span.Background == 0 && rule.background > 0) span.Background = rule.background;
            if (span.FontStyle == 0 && rule.fontStyle > 0) span.FontStyle = (int)rule.fontStyle;
        }
    }

    private IBrush? GetBrush(int colorId)
    {
        if (_theme == null) return null;
        if (_brushCache.TryGetValue(colorId, out var cached)) return cached;

        string? colorString = _theme.GetColor(colorId);
        if (string.IsNullOrEmpty(colorString)) return null;

        // TextMate 颜色是 #RRGGBB 或 #RRGGBBAA（RGBA）；Avalonia 的 Color.Parse 认 #AARRGGBB（ARGB）。
        // 9 位（#+8 位 hex）需要把前 6 位与后 2 位对调，否则 alpha 会被当成 RGB 前段
        if (colorString.Length == 9)
        {
            Span<char> normalized = stackalloc char[9];
            normalized[0] = '#';
            normalized[1] = colorString[7];
            normalized[2] = colorString[8];
            normalized[3] = colorString[1];
            normalized[4] = colorString[2];
            normalized[5] = colorString[3];
            normalized[6] = colorString[4];
            normalized[7] = colorString[5];
            normalized[8] = colorString[6];
            colorString = normalized.ToString();
        }

        if (!Color.TryParse(colorString, out var color)) return null;

        var brush = new ImmutableSolidColorBrush(color);
        _brushCache[colorId] = brush;
        return brush;
    }

    private static void ApplyFontStyle(VisualLineElement element, int textMateStyle)
    {
        var current = element.TextRunProperties.Typeface;
        var style = (FontStyle)textMateStyle;

        Avalonia.Media.FontStyle avaloniaStyle = current.Style;
        if ((style & FontStyle.Italic) != 0) avaloniaStyle = Avalonia.Media.FontStyle.Italic;

        var weight = current.Weight;
        if ((style & FontStyle.Bold) != 0) weight = FontWeight.Bold;

        if (avaloniaStyle == current.Style && weight == current.Weight) return;
        element.TextRunProperties.SetTypeface(new Typeface(current.FontFamily, avaloniaStyle, weight));
    }
}
