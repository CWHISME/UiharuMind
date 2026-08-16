/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Controls;

public partial class SimpleMarkdownViewer : UserControl
{
    /// <summary>
    /// 纯文本档：只是把已解析好的 markdown 换成一块纯文本显示，<b>不省解析</b>——
    /// 增量无论哪一档都照样喂进 <see cref="_markdownBuilder"/>，省下的只有该控件的
    /// 布局与绘制。所以它是用户偏好，不是性能档，也不该按"是否生成完毕"来切。
    /// </summary>
    public static readonly StyledProperty<bool> IsPlaintextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, bool>(nameof(IsPlaintext));

    public static readonly StyledProperty<string> MarkdownTextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, string>(nameof(MarkdownText));

    public bool? IsPlaintext
    {
        get => GetValue(IsPlaintextProperty);
        set => SetValue(IsPlaintextProperty, value);
    }

    public string MarkdownText
    {
        get => GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    private bool _isPlaintextCache = true;

    private ObservableStringBuilder _markdownBuilder = new ObservableStringBuilder();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        MarkdownTextRender.MarkdownBuilder = _markdownBuilder;
        UpdateCodeBlockTheme();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownTextProperty)
        {
            ForceSetText(change.GetNewValue<string>());
        }
        else if (change.Property == IsPlaintextProperty)
        {
            var isPlainText = change.GetNewValue<bool>();
            if (PlainTextBlock.IsVisible != isPlainText || MarkdownTextRender.IsVisible == isPlainText)
            {
                PlainTextBlock.IsVisible = isPlainText;
                MarkdownTextRender.IsVisible = !isPlainText;
            }

            _isPlaintextCache = isPlainText;
            if (_isPlaintextCache) PlainTextBlock.Text = _markdownBuilder.ToString();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = Math.Clamp(availableSize.Width - 30, 0, int.MaxValue);
        MarkdownTextRender.MaxWidth = Math.Clamp(maxWidth - 1, 0, int.MaxValue);
        MainPanel.MaxWidth = maxWidth;
        return base.MeasureOverride(availableSize);
    }

    public SimpleMarkdownViewer()
    {
        InitializeComponent();

        IsPlaintext = true;
        // var currentTheme = Application.Current.ActualThemeVariant;
        // var fontFamily = currentTheme.<FontFamily>("FontFamily");
        // FontManager.Current.DefaultFontFamily
        // MarkdownTextBlock.Container.AddFontFamily(new FontFamily(
        //     new Uri("avares://UiharuMind/Assets/Fonts"),
        //     "#Dream Han Sans CN"));
        // MarkdownTextBlock.Container.AddFontFamily(new FontFamily(
        //     new Uri("avares://UiharuMind/Assets/Fonts"),
        //     "#JetBrains Mono"));
        // MarkdownTextBlock.StylesheetLoad += OnStylesheetLoad;
        // MarkdownTextBlock.Container.AddFontFamily(PlainTextBlock.FontFamily);
        // MarkdownTextBlock.AddHandler(
        //     MarkdownRenderer.LinkCommandProperty,
        //     new EventHandler<LinkClickedEventArgs>(OnLinkClick),
        //     handledEventsToo: true);

        // _stopwatch.Start();
        // _scrollViewerAutoScrollHolder =
        //     new ScrollViewerAutoScrollHolder((ScrollViewer)this.LogicalChildren[0].LogicalChildren[0]);

        if (Application.Current != null) Application.Current.ActualThemeVariantChanged += OnThemeChanged;
    }

    public void ForceSetText(string text)
    {
        text ??= "";

        // 以 _markdownBuilder 的实际内容为准,不另外维护一份镜像字符串——
        // 该控件有 ForceSetText 与 AppendText 两个写入口,任何镜像状态都要求两处同步维护,
        // 漏一处就会静默失效(曾表现为新会话清不掉上一次的内容)。
        string current = _markdownBuilder.ToString();
        if (text == current)
        {
            if (IsPlaintext == true) PlainTextBlock.Text = text;
            return;
        }

        // 传入累积全文时只追加增量:LiveMarkdown 的 ObservableStringBuilder 本就是为
        // 增量追加设计的,每次 Clear + Append 全文会让它在每个 token 上重解析整篇
        // 并重建视觉树,成本随长度二次增长。
        if (text.Length > current.Length && text.StartsWith(current, StringComparison.Ordinal))
        {
            _markdownBuilder.Append(text[current.Length..]);
        }
        else
        {
            _markdownBuilder.Clear();
            _markdownBuilder.Append(text);
        }

        if (IsPlaintext == true) PlainTextBlock.Text = text;
    }

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _markdownBuilder.Append(text);
        if (IsPlaintext == true) PlainTextBlock.Text = _markdownBuilder.ToString();
    }

    public void Clear()
    {
        ForceSetText("");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateCodeBlockTheme();
    }

    private void UpdateCodeBlockTheme()
    {
        MarkdownTextRender.CodeBlockColorTheme = ApplicationThemeManager.IsDarkTheme()
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;
    }
}