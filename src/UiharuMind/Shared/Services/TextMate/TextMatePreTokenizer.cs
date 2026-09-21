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
using System.Diagnostics;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace UiharuMind.Shared.Services.TextMate;

/// <summary>
/// 预 tokenize 调度器：把 TextMate 语法高亮的成本从渲染管线里挪出来。
///
/// 背景：AvaloniaEdit 官方 TextMate 集成在渲染每个可视行时同步调
/// <c>model.GetLineTokens</c>，而拖选长行越过视口右边缘会触发上游 issue #606 的
/// 无限事件循环；tokenize 成本叠进循环后 UI 线程被填满。本类把 tokenize 放到
/// 装载后的空闲分帧里做，渲染时只查 <see cref="TextMateColorMapTransformer"/> 的表。
///
/// 调度模型照搬 <c>SimpleMarkdownViewer</c> 的 Pump：每帧最多跑一个时间片
/// （<see cref="TimeSliceMs"/>），跑不完 <c>Post</c> 下一帧，滚动与输入始终有机会插进来。
/// 编辑/追加会触发 <see cref="InvalidateFrom"/>，从变更行往后重 tokenize
/// （沿用前一行结束时的 TextMate 状态，多行结构带来的颜色漂移只影响变更点之后的区间）。
/// </summary>
public sealed class TextMatePreTokenizer : IDisposable
{
    private const int TimeSliceMs = 6;

    private readonly TextView _textView;
    private readonly TextMateColorMapTransformer _transformer;

    private TextDocument? _document;
    private IGrammar? _grammar;
    private Theme? _theme;

    /// <summary>每行 tokenize 结束时的 TextMate 状态（0 起；供下一行续跑）</summary>
    private readonly List<IStateStack?> _lineStates = new();

    /// <summary>下一个要 tokenize 的行（0 起）</summary>
    private int _nextLine;

    private bool _pumpScheduled;

    // 本时间片内已 tokenize 的行范围（1 起），Pump 末尾统一 Redraw
    private int _changedStart = int.MaxValue;
    private int _changedEnd = -1;

    public TextMatePreTokenizer(TextView textView, TextMateColorMapTransformer transformer)
    {
        _textView = textView;
        _transformer = transformer;
    }

    /// <summary>从第 0 行开始全量 tokenize（换文档/换语法时调用）。文档不可为 null</summary>
    public void Start(TextDocument document, IGrammar grammar, Theme theme)
    {
        _document = document;
        _grammar = grammar;
        _theme = theme;
        _nextLine = 0;
        _lineStates.Clear();
        _transformer.ClearAll();
        _transformer.SetTheme(theme);
        SchedulePump();
    }

    /// <summary>主题切换：scopes 已缓存，只重匹配颜色并整窗重绘，不重 tokenize</summary>
    public void SetTheme(Theme theme)
    {
        _theme = theme;
        _transformer.SetTheme(theme);
        if (_document == null || _document.LineCount <= 0) return;

        DocumentLine first = _document.GetLineByNumber(1);
        DocumentLine last = _document.GetLineByNumber(_document.LineCount);
        _textView.Redraw(first.Offset, (last.Offset + last.TotalLength) - first.Offset);
    }

    /// <summary>文档在 <paramref name="lineNumber0"/>（0 起）被改动：从该行往后重 tokenize</summary>
    public void InvalidateFrom(int lineNumber0)
    {
        if (_grammar == null || _document == null) return;
        if (lineNumber0 < 0) lineNumber0 = 0;
        if (lineNumber0 >= _document.LineCount) lineNumber0 = _document.LineCount - 1;

        int count = _lineStates.Count - lineNumber0;
        if (count > 0) _lineStates.RemoveRange(lineNumber0, count);

        _transformer.ClearFrom(lineNumber0 + 1);
        if (_nextLine > lineNumber0) _nextLine = lineNumber0;
        SchedulePump();
    }

    public void Dispose()
    {
        _document = null;
        _grammar = null;
        _theme = null;
        _lineStates.Clear();
        _pumpScheduled = false;
        _transformer.ClearAll();
    }

    private void SchedulePump()
    {
        if (_pumpScheduled) return;
        _pumpScheduled = true;
        Dispatcher.UIThread.Post(Pump, DispatcherPriority.Background);
    }

    private void Pump()
    {
        _pumpScheduled = false;
        if (_grammar == null || _document == null || _theme == null) return;

        _changedStart = int.MaxValue;
        _changedEnd = -1;

        var sw = Stopwatch.StartNew();
        while (_nextLine < _document.LineCount && sw.ElapsedMilliseconds < TimeSliceMs)
        {
            TokenizeLine(_nextLine);
            _nextLine++;
        }

        if (_changedEnd >= _changedStart)
        {
            DocumentLine first = _document.GetLineByNumber(_changedStart);
            DocumentLine last = _document.GetLineByNumber(_changedEnd);
            _textView.Redraw(first.Offset, (last.Offset + last.TotalLength) - first.Offset);
        }

        if (_nextLine < _document.LineCount) SchedulePump();
    }

    private void TokenizeLine(int lineIndex)
    {
        if (_document == null || _grammar == null) return;

        DocumentLine line = _document.GetLineByNumber(lineIndex + 1);
        string text = _document.GetText(line.Offset, line.Length);

        // TextMate 状态跨行累积：第 0 行传 null（初始状态），其余行用上一行结束的状态。
        // TokenizeLine 内部会克隆传入状态，不会改坏我们保存的那份。
        IStateStack? prevState = lineIndex == 0 ? null : _lineStates[lineIndex - 1];
        ITokenizeLineResult result = _grammar.TokenizeLine(new LineText(text), prevState, TimeSpan.MaxValue);

        EnsureStateCapacity(lineIndex + 1);
        _lineStates[lineIndex] = result.RuleStack;

        var spans = BuildSpans(result.Tokens, line.Length);
        _transformer.SetLine(lineIndex + 1, spans);

        if (lineIndex + 1 < _changedStart) _changedStart = lineIndex + 1;
        if (lineIndex + 1 > _changedEnd) _changedEnd = lineIndex + 1;
    }

    private void EnsureStateCapacity(int count)
    {
        while (_lineStates.Count < count) _lineStates.Add(null);
    }

    private static TextMateColorMapTransformer.ColorSpan[] BuildSpans(IToken[] tokens, int lineLength)
    {
        if (tokens == null || tokens.Length == 0)
            return [];

        var spans = new TextMateColorMapTransformer.ColorSpan[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            IToken token = tokens[i];
            int start = Math.Clamp(token.StartIndex, 0, lineLength);
            int end = Math.Clamp(token.EndIndex, start, lineLength);
            spans[i] = new TextMateColorMapTransformer.ColorSpan
            {
                Start = start,
                Length = end - start,
                Scopes = token.Scopes,
            };
        }
        return spans;
    }
}
