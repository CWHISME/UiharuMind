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
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using TheArtOfDev.HtmlRenderer.Avalonia;
using TheArtOfDev.HtmlRenderer.Core.Entities;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.ViewModels.UIHolder;

namespace UiharuMind.Views.Common;

public partial class SimpleMarkdownViewer : UserControl
{
    public static readonly StyledProperty<bool> IsPlaintextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, bool>(nameof(IsPlaintext));

    // public static readonly StyledProperty<bool> IsLoadingProperty =
    //     AvaloniaProperty.Register<SimpleMarkdownViewer, bool>(nameof(IsLoading));

    public static readonly StyledProperty<string> MarkdownTextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, string>(nameof(MarkdownText));

    public bool? IsPlaintext
    {
        get => GetValue(IsPlaintextProperty);
        set => SetValue(IsPlaintextProperty, value);
    }

    // public bool? IsLoading
    // {
    //     get => GetValue(IsLoadingProperty);
    //     set => SetValue(IsLoadingProperty, value);
    // }

    public string MarkdownText
    {
        get => GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    private string HtmlText =>
        MarkdownUtils.ToHtml(_textCache ?? "", Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
    // GetThemeSpecificHtml(Application.Current?.ActualThemeVariant, MarkdownUtils.ToHtml(_textCache));

    private string? _textCache;

    // private bool? _isLastPlaintextCache;
    private bool _isPlaintextCache = true;
    private bool _isLoadingCache = true;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        CheckUpdateValid();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // if (!IsInitialized) return;
        if (change.Property == MarkdownTextProperty)
        {
            _textCache = change.GetNewValue<string>();
            // Log.Debug($"MarkdownText changed: {_textCache}");
            if (IsInitialized) CheckUpdateValid();
        }
        else if (change.Property == IsPlaintextProperty)
        {
            // Log.Debug($"IsPlaintext changed: {_isPlaintextCache}");
            var isPlainText = change.GetNewValue<bool>();
            if (PlainTextBlock.IsVisible != isPlainText || MarkdownTextBlock.IsVisible == isPlainText)
            {
                PlainTextBlock.IsVisible = isPlainText;
                MarkdownTextBlock.IsVisible = !isPlainText;
            }

            _isPlaintextCache = isPlainText;
            if (IsInitialized) CheckUpdateValid();
        }
        // else if (change.Property == IsLoadingProperty)
        // {
        //     SetLoadingState(change.GetNewValue<bool>());
        // }
    }

    private void CheckUpdateValid()
    {
        // if (_isPlaintextCache != null)
        // {
        if (_isPlaintextCache)
        {
            PlainTextBlock.Text = _textCache;
        }
        else
        {
            // MarkdownTextBlock.MaxWidth = this.Bounds.Width;
            if (_textCache != null) MarkdownTextBlock.Text = HtmlText; //MarkdownUtils.ToHtml(_textCache);
        }

        SetLoadingState(string.IsNullOrEmpty(_textCache));
        // }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        MarkdownTextBlock.MaxWidth = availableSize.Width - 20;
        return base.MeasureOverride(availableSize);
    }

    private void SetLoadingState(bool isLoading)
    {
        if (isLoading == _isLoadingCache) return;
        _isLoadingCache = isLoading;
        LoadingEffect.IsLoading = isLoading;
        LoadingEffect.IsVisible = isLoading;
    }
    // public string HtmlText
    // {
    //     get => MarkdownUtils.ToHtml(MarkdownText);
    // }

    private ScrollViewerAutoScrollHolder _scrollViewerAutoScrollHolder;
    // private Stopwatch _stopwatch = new Stopwatch();

    static SimpleMarkdownViewer()
    {
        HtmlRender.AddFontFamily(new FontFamily(
            new Uri("avares://UiharuMind/Assets/Fonts"),
            "#Dream Han Sans CN"));
        HtmlRender.AddFontFamily(new FontFamily(
            new Uri("avares://UiharuMind/Assets/Fonts"),
            "#JetBrains Mono"));
    }

    public SimpleMarkdownViewer()
    {
        InitializeComponent();

        IsPlaintext = true;
        SetLoadingState(false);
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


        // _stopwatch.Start();
        _scrollViewerAutoScrollHolder =
            new ScrollViewerAutoScrollHolder((ScrollViewer)this.LogicalChildren[0].LogicalChildren[0]);

        if (Application.Current != null) Application.Current.ActualThemeVariantChanged += OnThemeChanged;
    }

    // private void OnStylesheetLoad(object? sender, HtmlRendererRoutedEventArgs<HtmlStylesheetLoadEventArgs> e)
    // {
    //     e.Event.SetStyleSheet =
    //         MarkdownUtils.GetGlobalStylesheet(e.Event.Src,
    //             Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
    // }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        CheckUpdateValid();
    }


    // private bool _change = false;
    private string GetThemeSpecificHtml(ThemeVariant? theme, string text)
    {
        // <div style='background-color: #333; color: #fff; '>
        // <div style='background-color: #fff; color: #000; '>
        if (theme == null) return text;
        // if (!_change)
        // {
        //     _change = true;
        return MarkdownUtils.ToHtml(text, theme == ThemeVariant.Dark);
        // }
        // _change = false;
        // 分为黑白主题，分别定义样式，字体大小、颜色、上下边距(这是因为默认的 HtmlRenderer 似乎会将上下边距增加 20px)
        //  <head><style>p {{font-size: 14px;margin:-20px 0px;}}</style></head>
        // if (theme == ThemeVariant.Dark)
        // {
        //     //去掉第一个和最后一个 p 标签的上下边距，避免不整齐
        //     return @$"
        //              <head><style>* {{font-family: 'Dream Han Sans CN';font-size: 14px;}}
        //              </style></head>
        //              <body style='color: #fff;'>
        //                  {text}
        //              </body>";
        // }
        //
        // return @$"
        //          <head><style>* {{font-family: 'Dream Han Sans CN';font-size: 14px}}
        //              </style></head>
        //              <body style='color: #000;'>
        //                  {text}
        //              </body>";
    }
}