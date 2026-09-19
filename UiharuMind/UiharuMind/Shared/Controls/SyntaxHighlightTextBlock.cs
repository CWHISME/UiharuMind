using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 带语法高亮的可选中文本块：把 <see cref="TextBlock.Text"/> 按语言 tokenize 成带色 <see cref="Run"/>。
/// 只做呈现，不改选中/复制（SelectableTextBlock 的看家本领原样保留）。
///
/// 高亮委托 <see cref="SyntaxHighlighting"/>（LiveMarkdown 自带的那套 TextMate 管线，与 markdown 代码块
/// 同一实现，两处观感因此一致）。它是「文本 → 彩色 Inline」的非编辑器实现，正好补上
/// <see cref="TextMateSyntaxService"/>（全文窗那条 AvaloniaEdit 绑定管线）不覆盖的场景。
///
/// 语言认不出的降级：认不出的名字也会走 fallback 语法，把整段染成<b>主题默认前景色</b>
/// （与纯文本视觉等价）。因此父容器若自定义了 Foreground（如 muted 的灰），会被覆盖成默认色——
/// 用本控件的地方不要依赖 Foreground 继承，要灰就让它在可识别语法里自然呈现。
/// </summary>
public sealed class SyntaxHighlightTextBlock : SelectableTextBlock
{
    /// <summary>超过这个字符数一律不上高亮。预览已被截断（2KB/40 行），这道闸是防未来调用方</summary>
    internal const int MaxHighlightChars = 8 * 1024;

    /// <summary>语言来源：文件名、扩展名或语言标识（<c>Foo.cs</c> / <c>.json</c> / <c>json</c>）。空或认不出 → 纯文本</summary>
    public static readonly StyledProperty<string?> SourceNameProperty =
        AvaloniaProperty.Register<SyntaxHighlightTextBlock, string?>(nameof(SourceName));

    static SyntaxHighlightTextBlock()
    {
        TextProperty.Changed.AddClassHandler<SyntaxHighlightTextBlock>(static (o, _) => o.Rebuild());
        SourceNameProperty.Changed.AddClassHandler<SyntaxHighlightTextBlock>(static (o, _) => o.Rebuild());
    }

    /// <summary>
    /// 语言来源（文件名/扩展名/语言标识）。null 或认不出时按纯文本渲染
    /// </summary>
    public string? SourceName
    {
        get => GetValue(SourceNameProperty);
        set => SetValue(SourceNameProperty, value);
    }

    /// <summary>样式仍按 `SelectableTextBlock` 命中（mono/muted 等类样式原样适用）</summary>
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

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
        // 主题切换：预览很小，scopes 重新匹配颜色的成本可忽略，整体重建最省心
        Rebuild();
    }

    private void Rebuild()
    {
        // 未挂树时构建是白费（DataTemplate 里大量创建-卸载）；Text 绑定变化也可能发生在挂树前
        if (!_attached) return;

        string? text = Text;
        if (string.IsNullOrEmpty(text) || text.Length > MaxHighlightChars)
        {
            Inlines = null; // 落回 Text 属性渲染纯文本
            return;
        }

        string? language = ResolveLanguageName(SourceName);
        if (language == null)
        {
            Inlines = null;
            return;
        }

        // 每行一个 Run，行间插 LineBreak：SyntaxHighlighting 按 Run 逐行 tokenize，
        // 行间 TextMate 状态跨行累积（多行字符串/注释的颜色不漂）。行内再被拆成多个带色 Run
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inlines = new InlineCollection();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            inlines.Add(new Run(lines[i]));
        }

        SyntaxHighlighting.Create(language).FormatInlines(inlines, GetCurrentTheme());
        Inlines = inlines;
    }

    /// <summary>
    /// 从语言来源里取出 TextMate 认得出的语言标识。只认「文件名/扩展名」这类确定信息，
    /// 不猜内容（与「结果的语言来源」词条同口径，见 docs/CONTEXT.md）。
    /// </summary>
    /// <param name="sourceName">文件名、扩展名或语言标识，可带路径</param>
    /// <returns>语言标识（<c>cs</c> / <c>json</c>…）；认不出返回 null</returns>
    internal static string? ResolveLanguageName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return null;

        string extension = sourceName[0] == '.'
            ? sourceName
            : Path.GetExtension(sourceName);

        // 有扩展名按扩展名；没有（json / cs 这类裸语言标识）整体当语言名。
        // 目录名会落进这里，但 TextMate 认不出时会 fallback 成默认前景色（约等于纯文本），
        // 所以无需特判目录；见类注释的降级说明
        return (string.IsNullOrEmpty(extension) ? sourceName : extension)
            .TrimStart('.')
            .ToLowerInvariant();
    }

    private static ThemeName GetCurrentTheme()
    {
        return ApplicationThemeManager.IsDarkTheme() ? ThemeName.DarkPlus : ThemeName.LightPlus;
    }
}