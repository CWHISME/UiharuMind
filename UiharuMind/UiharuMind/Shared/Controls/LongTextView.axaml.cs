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
using Avalonia.Data;
using Avalonia.Interactivity;
using System.IO;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 长文本视图：全仓唯一「吃得下大文本」的文本控件。
///
/// <c>SelectableTextBlock</c> 与 <c>TextBox</c> 都没有文本虚拟化，几十万字塞进去必然卡死；
/// 这里的内核 <see cref="AvaloniaEdit.TextEditor"/> 按行虚拟化，屏幕外的行一行都不用测量。
/// 单行过长会绕过按行虚拟化，故只读时先过 <see cref="LongLineWrapper"/> 硬切。
/// </summary>
public partial class LongTextView : UserControl
{
    private bool _isSyncingText; //防止属性与文档互相回灌
    private bool _scrollToTopPending; //在加载完成前请求过回顶，等 Loaded 补做
    private TextMate.Installation? _textMate; //装上才有高亮，不高亮时彻底卸掉
    private TextDocument? _highlightedDocument; //当前这份安装绑的是哪个文档，换文档要重装

    /// <summary>正文</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LongTextView, string?>(nameof(Text),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>是否可编辑，默认只读</summary>
    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<LongTextView, bool>(nameof(IsEditable));

    /// <summary>是否自动换行，默认关闭（走横向滚动）</summary>
    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<LongTextView, bool>(nameof(WordWrap),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>是否显示行号，默认显示</summary>
    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<LongTextView, bool>(nameof(ShowLineNumbers), true);

    /// <summary>是否显示自带工具条，默认显示</summary>
    public static readonly StyledProperty<bool> IsToolbarVisibleProperty =
        AvaloniaProperty.Register<LongTextView, bool>(nameof(IsToolbarVisible), true);

    /// <summary>语法高亮的语言来源（文件名或扩展名），默认无高亮</summary>
    public static readonly StyledProperty<string?> SyntaxSourceNameProperty =
        AvaloniaProperty.Register<LongTextView, string?>(nameof(SyntaxSourceName));

    /// <summary>超过这个字符数一律不上高亮：全文窗的立身之本是"几十万字秒开"，不能为配色让路</summary>
    private const int MaxHighlightChars = 256 * 1024;

    // 语法表与主题按主题名全进程共用一份:构造 RegistryOptions 要加载主题与整套语法定义,
    // 按控件实例建的话,多开几个全文窗就是重复加载几份
    private static RegistryOptions? _sharedRegistryOptions;
    private static ThemeName _sharedRegistryTheme;

    /// <summary>正文</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>是否可编辑，默认只读</summary>
    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>是否自动换行，默认关闭（走横向滚动）</summary>
    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    /// <summary>是否显示行号，默认显示</summary>
    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    /// <summary>
    /// 是否显示自带工具条。宿主自己有标题栏时关掉它，把
    /// <see cref="CopyAll"/> / <see cref="OpenSearch"/> / <see cref="WordWrap"/> 挂到标题栏上，
    /// 省掉一整条只有几十像素高的横带
    /// </summary>
    public bool IsToolbarVisible
    {
        get => GetValue(IsToolbarVisibleProperty);
        set => SetValue(IsToolbarVisibleProperty, value);
    }

    /// <summary>
    /// 语法高亮的语言来源：给一个<b>文件名或扩展名</b>（<c>Foo.cs</c> / <c>.cs</c> / <c>.json</c>），
    /// 由 TextMate 按扩展名挑语法。为空、认不出、或正文超过
    /// <see cref="MaxHighlightChars"/> 时一律不高亮。
    ///
    /// 用扩展名而不是语言名，是因为调用方手上现成的信息就是文件路径（工具调用的 <c>filePath</c> 参数），
    /// 按扩展名选是<b>确定的</b>，按内容猜语言会猜错。
    ///
    /// 配色走 TextMate 主题，与 markdown 代码块同一套（DarkPlus / LightPlus），跟随应用主题切换。
    /// </summary>
    public string? SyntaxSourceName
    {
        get => GetValue(SyntaxSourceNameProperty);
        set => SetValue(SyntaxSourceNameProperty, value);
    }

    public LongTextView()
    {
        InitializeComponent();
        // AvaloniaEdit 承袭代码编辑器的习惯,默认允许在文末之后再滚一整屏(方便把最后一行顶到屏幕中间)。
        // 这里读的是文档不是代码,那一整屏空白只会让人以为还有内容
        Editor.Options.AllowScrollBelowDocument = false;
        Editor.IsReadOnly = !IsEditable;
        Editor.WordWrap = WordWrap;
        Editor.ShowLineNumbers = ShowLineNumbers;
        Editor.TextChanged += OnEditorTextChanged;
        ApplyText();
    }

    /// <summary>
    /// 把视图滚回顶部。窗口复用时换源必须调，否则新内容停在上一份的滚动位置
    /// </summary>
    public void ScrollToTop()
    {
        Editor.CaretOffset = 0;
        // 没加载完时编辑器内部还没有滚动条可滚，记一笔等 Loaded 补做：
        // 缓存窗口换源正好落在这一档（先 SetSource 再 Show）
        if (Editor.IsLoaded) Editor.ScrollToHome();
        else _scrollToTopPending = true;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (!_scrollToTopPending) return;
        _scrollToTopPending = false;
        Editor.ScrollToHome();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            ApplyText();
            ApplySyntax(); //正文换了要重判大小闸门
        }
        else if (change.Property == IsEditableProperty)
        {
            Editor.IsReadOnly = !IsEditable;
            // 只读与可编辑喂进去的文本不是同一份（见 ApplyText），改档要重喂
            ApplyText();
        }
        else if (change.Property == WordWrapProperty)
        {
            Editor.WordWrap = WordWrap;
        }
        else if (change.Property == ShowLineNumbersProperty)
        {
            Editor.ShowLineNumbers = ShowLineNumbers;
        }
        else if (change.Property == SyntaxSourceNameProperty)
        {
            ApplySyntax();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Application.Current != null) Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        ApplySyntax();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current != null) Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // TextMate 的配色是安装时固化进去的,换主题只能整份重装
        UninstallTextMate();
        ApplySyntax();
    }

    /// <summary>
    /// 按当前语言与主题重装 TextMate。装不上（没给扩展名、认不出、文本太大）就彻底卸掉，
    /// 回到纯文本——宁可不高亮，也不让大文本卡住
    /// </summary>
    private void ApplySyntax()
    {
        string? scope = ResolveScope();
        if (scope == null)
        {
            UninstallTextMate();
            return;
        }

        // 安装是绑在当时那个 Document 上的,而 ApplyText 每次都换一份新文档(为清撤销栈与选区)。
        // 沿用旧安装的话,分词器的行状态与当前文档对不上——表现为同一个关键字有的行上色、有的行不上
        if (_textMate == null || !ReferenceEquals(_highlightedDocument, Editor.Document))
        {
            UninstallTextMate();
            _textMate = Editor.InstallTextMate(GetRegistryOptions());
            _highlightedDocument = Editor.Document;
        }

        _textMate.SetGrammar(scope);
    }

    private string? ResolveScope()
    {
        if (string.IsNullOrEmpty(SyntaxSourceName)) return null;
        if ((Text?.Length ?? 0) > MaxHighlightChars) return null;

        string extension = Path.GetExtension(SyntaxSourceName);
        if (string.IsNullOrEmpty(extension)) return null;

        RegistryOptions options = GetRegistryOptions();
        Language? language = options.GetLanguageByExtension(extension);
        return language == null ? null : options.GetScopeByLanguageId(language.Id);
    }

    //注册表同时决定了配色。主题名与 markdown 代码块用的是同一套,两处观感因此一致
    private static RegistryOptions GetRegistryOptions()
    {
        ThemeName themeName = ApplicationThemeManager.IsDarkTheme() ? ThemeName.DarkPlus : ThemeName.LightPlus;
        if (_sharedRegistryOptions == null || _sharedRegistryTheme != themeName)
        {
            _sharedRegistryOptions = new RegistryOptions(themeName);
            _sharedRegistryTheme = themeName;
        }

        return _sharedRegistryOptions;
    }

    private void UninstallTextMate()
    {
        if (_textMate == null) return;
        _textMate.Dispose();
        _textMate = null;
        _highlightedDocument = null;
    }

    private void ApplyText()
    {
        if (_isSyncingText) return;

        // 只读时才硬切超长行：可编辑时切进去的换行会顺着双向绑定写回数据源，那是篡改用户数据。
        // 可编辑档靠 WordWrap 解决超长行的观感，性能上编辑对象本来也不该是几百 KB 的日志
        string display = IsEditable ? Text ?? string.Empty : LongLineWrapper.Wrap(Text);

        _isSyncingText = true;
        Editor.Document = new TextDocument(display); //换新文档顺带清掉上一份的撤销栈与选区
        _isSyncingText = false;

        ScrollToTop();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isSyncingText || !IsEditable) return;

        _isSyncingText = true;
        SetCurrentValue(TextProperty, Editor.Document.Text);
        _isSyncingText = false;
    }

    /// <summary>
    /// 复制全文。复制的是原始 <see cref="Text"/> 而非文档内容——
    /// 只读档的文档里带着 <see cref="LongLineWrapper"/> 硬切进去的换行，那不该进剪贴板
    /// </summary>
    public void CopyAll()
    {
        App.Clipboard.CopyToClipboard(Text ?? string.Empty, true);
    }

    /// <summary>
    /// 打开搜索面板。
    ///
    /// 用的是 <see cref="AvaloniaEdit.TextEditor"/> 在 <c>OnApplyTemplate</c> 里<b>已经装好</b>的那一个。
    /// 千万别再调 <c>SearchPanel.Install</c>：它每调一次就新建一个面板<b>并注册一套按键绑定</b>，
    /// 结果是工具条按钮和 Ctrl+F 各开一个、两个面板叠在一起。
    /// </summary>
    public void OpenSearch()
    {
        Editor.SearchPanel?.Open();
    }

    private void CopyButton_Click(object? sender, RoutedEventArgs e) => CopyAll();

    private void SearchButton_Click(object? sender, RoutedEventArgs e) => OpenSearch();
}
