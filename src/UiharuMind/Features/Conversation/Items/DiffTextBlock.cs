using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using UiharuMind.Shared.Controls.TextRendering;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// diff 行集合的整块可选中渲染：单块文本 + 行级自绘背景（<see cref="DecoratedTextBlock"/>），
/// 增/删行的背景铺满整行、前景仍由 Run 上色（SemiGreen/SemiRed 主题资源）。
///
/// 演进：逐行 TextBlock 的 ItemsControl（整行背景但不可复制、80 个常驻控件）→ 单块 Inlines
/// （可跨行复制，但 Run.Background 只画字形框）→ 按字符跨度自绘（LiveMarkdown 算法），
/// 复制、性能、整行观感三者兼得。见 ADR 0035。
/// </summary>
public sealed class DiffTextBlock : DecoratedTextBlock
{
    /// <summary>要渲染的 diff 行；为空或 null 时整块清空</summary>
    public static readonly StyledProperty<IReadOnlyList<DiffLineView>?> LinesProperty =
        AvaloniaProperty.Register<DiffTextBlock, IReadOnlyList<DiffLineView>?>(nameof(Lines));

    private const string AddedBackgroundKey = "SemiGreen0";
    private const string AddedForegroundKey = "SemiGreen7";
    private const string RemovedBackgroundKey = "SemiRed0";
    private const string RemovedForegroundKey = "SemiRed7";

    static DiffTextBlock()
    {
        LinesProperty.Changed.AddClassHandler<DiffTextBlock>(static (o, _) => o.Rebuild());
    }

    /// <summary>
    /// 要渲染的 diff 行
    /// </summary>
    public IReadOnlyList<DiffLineView>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public DiffTextBlock()
    {
        // 对齐旧 diffline 的观感：字体继承自 mono 类（已统一 MainFont）；diff 行维持 12（紧凑列表，库代码块的 14 不适用这里），
        // 内边距沿用旧样式的 4,0。LineHeight 手动给定：单块行高默认贴身（lineGap 小），
        // 背景铺满后视觉偏挤；调大到 1.4x 行距，背景块随 TextLine.Height 一起变高，行与行自然拉开。
        // IBeam 光标不由这里给：StyleKey 已指回 SelectableTextBlock，主题的 ControlTheme
        // 按 IsEnabled 给 IBeam；构造里 new Cursor 要平台服务，还会把本类挡在单元测试外
        // Transparent 背景不画任何东西，但让整块（含空白区）可命中：Background 为 null 时
        // 空白处没有绘制操作、点下去落到父容器，拖选必须从字形上起笔
        Background = Brushes.Transparent;
        FontSize = 12;
        LineHeight = 17;
        Padding = new Thickness(4, 0);
    }

    private bool _attached; // 是否挂了视觉树（IsAttachedToVisualTree 不是公开 API，自记一份）

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (Application.Current != null) Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        if (Application.Current != null) Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // 增删前景/背景取自主题变体资源，切主题后重解析并重建
        Rebuild();
    }

    private void Rebuild()
    {
        if (!_attached) return;

        IReadOnlyList<DiffLineView>? lines = Lines;
        if (lines == null || lines.Count == 0)
        {
            Decorations = null;
            Inlines = null;
            return;
        }

        IBrush? addedBg = TryGetBrush(AddedBackgroundKey);
        IBrush? addedFg = TryGetBrush(AddedForegroundKey);
        IBrush? removedBg = TryGetBrush(RemovedBackgroundKey);
        IBrush? removedFg = TryGetBrush(RemovedForegroundKey);

        Decorations = BuildDecorations(lines, addedBg, removedBg);
        Inlines = BuildInlines(lines, addedFg, removedFg);
    }

    /// <summary>
    /// 每行的整行背景装饰段（<see cref="TextBackgroundSpan"/>，铺满行宽）：
    /// 增 = 绿底、删 = 红底、context 无装饰。字符偏移按 <see cref="BuildInlines"/> 的
    /// Inlines.Text 拼接规则（每行文本 + 行间一个换行）同步计算——两处必须一致，
    /// 否则背景与文字错位
    /// </summary>
    internal static IReadOnlyList<TextBackgroundSpan> BuildDecorations(
        IReadOnlyList<DiffLineView> lines,
        IBrush? addedBackground,
        IBrush? removedBackground)
    {
        var decorations = new List<TextBackgroundSpan>();
        int offset = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string text = lines[i].Prefix + lines[i].Text;
            IBrush? background = lines[i].IsAdded ? addedBackground
                : lines[i].IsRemoved ? removedBackground
                : null;

            if (background != null && text.Length > 0)
            {
                decorations.Add(new TextBackgroundSpan
                {
                    Start = offset,
                    Length = text.Length,
                    Background = background,
                    FillRowWidth = true,
                });
            }

            offset += text.Length;
            if (i < lines.Count - 1) offset += 1; // 行间 LineBreak → 文本源里一个换行符
        }

        return decorations;
    }

    /// <summary>
    /// 把 diff 行构建成单块 Inlines（行间 LineBreak；增/删行只设前景——背景由 Decorations 自绘）。
    /// 抽出为纯静态便于测试；与 <see cref="BuildDecorations"/> 共用同一行文本拼接规则
    /// </summary>
    internal static InlineCollection BuildInlines(
        IReadOnlyList<DiffLineView> lines,
        IBrush? addedForeground,
        IBrush? removedForeground)
    {
        var inlines = new InlineCollection();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());

            DiffLineView line = lines[i];
            var run = new Run(line.Prefix + line.Text);
            if (line.IsAdded)
            {
                if (addedForeground != null) run.Foreground = addedForeground;
            }
            else if (line.IsRemoved)
            {
                if (removedForeground != null) run.Foreground = removedForeground;
            }
            // context 行不设色，继承块默认

            inlines.Add(run);
        }

        return inlines;
    }

    /// <summary>
    /// 按当前主题显式解析笔刷。<b>必须传显式 variant</b>：<c>TryFindResource(key, out)</c>
    /// 传的是 <c>theme: null</c>，字典遇到 null 直接走 Default（浅色）分支，深色下永远拿到浅色值——
    /// 重启也一样，不是切换刷新问题。<c>DynamicResource</c> 内部用的是 <c>ActualThemeVariant</c>
    /// 显式查，所以旧样式是对的。从本控件出发查（而不用 <c>Application.Current</c>），
    /// 口径与挂在这里的 <c>DynamicResource</c> 一致，窗口级主题覆盖也不会错
    /// </summary>
    private IBrush? TryGetBrush(string key)
    {
        return this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;
    }
}