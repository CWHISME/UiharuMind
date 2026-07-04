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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;
using TheArtOfDev.HtmlRenderer.Avalonia;
using TheArtOfDev.HtmlRenderer.Core.Entities;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Services;
using UiharuMind.Utils.Tools;
using UiharuMind.ViewModels.UIHolder;

namespace UiharuMind.Views.Common;

public partial class SimpleMarkdownViewer : UserControl
{
    public static readonly StyledProperty<bool> IsPlaintextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, bool>(nameof(IsPlaintext));

    public static readonly StyledProperty<bool> IsThinkingRemoveProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, bool>(nameof(IsThinkingRemove));

    // public static readonly StyledProperty<bool> IsLoadingProperty =
    //     AvaloniaProperty.Register<SimpleMarkdownViewer, bool>(nameof(IsLoading));

    public static readonly StyledProperty<string> MarkdownTextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, string>(nameof(MarkdownText));

    public bool? IsPlaintext
    {
        get => GetValue(IsPlaintextProperty);
        set => SetValue(IsPlaintextProperty, value);
    }

    public bool? IsThinkingRemove
    {
        get => GetValue(IsThinkingRemoveProperty);
        set => SetValue(IsThinkingRemoveProperty, value);
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

    // /// <summary>
    // /// 更简单的设置 MarkdownText，不走 avalonia 绑定机制
    // /// </summary>
    // public string? SimpleSetMarkdownText
    // {
    //     set
    //     {
    //         _textCache = value;
    //         DelayCheckUpdate();
    //     }
    //     get => _textCache;
    // }

    // private string HtmlText =>
    //     MarkdownUtils.ToHtml(_textCache ?? "", ApplicationThemeManager.IsDarkTheme(),
    //         IsThinkingRemove ?? false, out _isThinking);
    // GetThemeSpecificHtml(Application.Current?.ActualThemeVariant, MarkdownUtils.ToHtml(_textCache));

    // private string? _textCache;
    // private StringBuilder _textCache = new StringBuilder();

    // private bool? _isLastPlaintextCache;
    private bool _isPlaintextCache = true;
    private bool _isLoadingCache = true;
    private bool _isThinking = false;

    private bool _isLoaded = false;

    private ValueUiDelayUpdater<string> _valueUiDelayUpdater;
    private ObservableStringBuilder _markdownBuilder = new ObservableStringBuilder();

    // private List<ThemeName> _themeNames = new List<ThemeName>(); 

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _isLoaded = true;
        // _themeNames.AddRange(EnumHelper.GetValues<ThemeName>());
        // ThemeComboBox.ItemsSource = _themeNames;
        // ThemeComboBox.SelectionChanged += (x, y) =>
        // {
        //     ThemeName themeName = (ThemeName)ThemeComboBox.SelectedItem;
        //     MarkdownTextBlock.CodeBlockColorTheme = themeName;
        // };

        MarkdownTextBlock.MarkdownBuilder = _markdownBuilder;
        // MarkdownTextBlock.CodeBlockColorTheme = ThemeName.LightPlus;
        // CheckUpdateValid();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _isLoaded = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // if (!IsInitialized) return;
        if (change.Property == MarkdownTextProperty)
        {
            // _textCache = change.GetNewValue<string>();
            //Log.Debug($"MarkdownText changed: {_textCache}");
            // CheckUpdateValid();
            ForceSetText(change.GetNewValue<string>());
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
            // CheckUpdateValid();
            // ForceSetText(_markdownBuilder.ToString());
            if (_isPlaintextCache) PlainTextBlock.Text = _markdownBuilder.ToString();
        }
        else if (change.Property == IsThinkingRemoveProperty)
        {
            // CheckUpdateValid();
            // ForceSetText(_markdownBuilder.ToString());
            if (_isPlaintextCache) PlainTextBlock.Text = _markdownBuilder.ToString();
        }

        // Log.Debug(
        //     $"SimpleMarkdownViewer property changed: {change.Property.Name}  PlainTextBlock.Text:{PlainTextBlock?.Text}");
        // else if (change.Property == IsLoadingProperty)
        // {
        //     SetLoadingState(change.GetNewValue<bool>());
        // }
    }

    private void SetText(string obj)
    {
        // CheckUpdateValid();
        ForceSetText(obj);
    }

    // private async void DelayCheckUpdate()
    // {
    //     await _valueUiDelayUpdater.UpdateValue(_textCache);
    // }

    // private void CheckUpdateValid()
    // {
    //     if (!_isLoaded) return;
    //     // if (_isPlaintextCache != null)
    //     // {
    //     if (_isPlaintextCache)
    //     {
    //         PlainTextBlock.Text += _textCache;
    //     }
    //     else
    //     {
    //         // MarkdownTextBlock.MaxWidth = this.Bounds.Width;
    //         // if (_textCache != null)
    //         // {
    //         // MarkdownTextBlock.MarkdownBuilder = HtmlText; //MarkdownUtils.ToHtml(_textCache);
    //         _markdownBuilder.Append(_textCache.ToString());
    //         // }
    //     }
    //
    //     SetLoadingState(string.IsNullOrEmpty(_textCache)); // || _isThinking);
    //     // }
    // }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = Math.Clamp(availableSize.Width - 30, 0, int.MaxValue);
        MarkdownTextBlock.MaxWidth = Math.Clamp(maxWidth - 1, 0, int.MaxValue);
        MainPanel.MaxWidth = maxWidth;
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

    // private ScrollViewerAutoScrollHolder _scrollViewerAutoScrollHolder;


    // private Stopwatch _stopwatch = new Stopwatch();

    static SimpleMarkdownViewer()
    {
    }

    public SimpleMarkdownViewer()
    {
        // Log.Debug("SimpleMarkdownViewer created");
        InitializeComponent();

        IsPlaintext = true;
        _isThinking = false;
        SetLoadingState(false);
        _valueUiDelayUpdater = new ValueUiDelayUpdater<string>(SetText, 100);
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
        // _scrollViewerAutoScrollHolder =
        //     new ScrollViewerAutoScrollHolder((ScrollViewer)this.LogicalChildren[0].LogicalChildren[0]);

        if (Application.Current != null) Application.Current.ActualThemeVariantChanged += OnThemeChanged;
    }

    public void ForceSetText(string text)
    {
        // _textCache = text;
        _markdownBuilder.Clear();
        _markdownBuilder.Append(text);
        if (IsPlaintext == true) PlainTextBlock.Text = text;
    }

    public void AppendText(string text)
    {
        _markdownBuilder.Append(text);
        if (IsPlaintext == true) PlainTextBlock.Text = _markdownBuilder.ToString();
    }

    public void Clear()
    {
        ForceSetText("");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // MarkdownTextBlock.CodeBlockColorTheme = ApplicationThemeManager.IsDarkTheme() ? ThemeName.DarkPlus : ThemeName.LightPlus;
        // CheckUpdateValid();
    }


    private string GetThemeSpecificHtml(ThemeVariant? theme, string text)
    {
        if (theme == null) return text;
        return MarkdownUtils.ToHtml(text, ApplicationThemeManager.IsDarkTheme(theme),
            ChatSettingConfig.Current.IsChatNotShowThinking, out _isThinking);
    }
}