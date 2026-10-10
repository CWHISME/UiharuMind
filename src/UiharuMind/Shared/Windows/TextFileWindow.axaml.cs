/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 文本窗：磁盘文本文件的编辑，也是长文本的只读查看（<see cref="ShowText"/>）与流式思考过程的跟读（<see cref="ShowLive"/>）。
/// 与 <see cref="StringContentEditWindow"/>（会话内短文本编辑）各归各位。
/// 无路径也能开（空文档、标题显示未命名），文件从「更多 / 打开」或直接拖进来。
///
/// 只读查看从前是单独的全文窗与思考窗，没合并是怕性能：可编辑档不能硬切超长行（切进去的换行会写回文件），
/// 几十万字一行的结果在编辑器里排不动。现在只读文档照旧以只读档喂给编辑器（<see cref="LongTextView"/>
/// 会把超长行硬切开），与原来同一条路，顺带有了 Markdown 预览与另存为。
///
/// 分工：这份文档是什么（来源、编码、改没改）归 <see cref="TextFileDocument"/>；原文与预览两种看法、
/// 定位与跟底归 <see cref="TextDocumentView"/>；本文件只管装载与显示，保存在 <c>TextFileWindow.Saving.cs</c>，
/// 按钮、菜单、快捷键与拖放在 <c>TextFileWindow.Actions.cs</c>，磁盘上被外部改了的处理在 <c>TextFileWindow.Watching.cs</c>。
///
/// 保存走 <see cref="TextFileCodec"/>：原编码、原 BOM 写回，行尾不归一；原编码存不下的字先问要不要改存 UTF-8。
/// 关闭前未保存有三态确认（保存 / 不保存 / 取消，见 <see cref="EConfirmChoice"/>）。
/// </summary>
public partial class TextFileWindow : QuickWindowBase
{
    private TextFileDocument _document = TextFileDocument.Untitled();
    private ILiveText? _live; //正在跟读的流式正文；换源、隐藏时退订
    private bool _suppressDirty; //装载期间不把程序化设置当用户编辑
    private bool _closingPromptOpen; //防止 OnClosing 里弹确认被重复触发
    private readonly TextFileSettingConfig _setting = TextFileSettingConfig.Current;

    private IMessageService MessageService => App.Services.GetRequiredService<IMessageService>();

    public TextFileWindow()
    {
        InitializeComponent();
        DocumentView.TextView.TextChanged += (_, _) =>
        {
            // 只读文档没有用户编辑，流式追加也不算改动
            if (_suppressDirty || _document.IsReadOnly) return;
            _document.IsDirty = true;
            RefreshDirtyState();
            RefreshMenuStates();
            UpdateStatusBar();
        };
        DocumentView.PreviewChanged += (_, preview) => PreviewButton.IsChecked = preview;
        DocumentView.TextView.Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();
        DocumentView.TextView.Editor.TextArea.SelectionChanged += (_, _) => UpdateCaretStatus();
        // 全局偏好：别的文本窗改了设置，这个还开着的窗口跟着变。
        // 进缓存只是隐藏，订阅保留；被缓存淘汰真关时在 OnClosed 退订，否则设置单例会把整扇窗连同预览树钉住
        _setting.PropertyChanged += OnGlobalSettingChanged;

        // 整窗接受文件拖放（对照 ConversationView）：DragOver 只在文件拖放时介入，
        // 高亮蒙版盖顶但不拦事件，可见性由三个拖放 handler 控制
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Activated += OnWindowActivated;
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
    }

    /// <summary>
    /// 打开一个空文档（普通文本编辑器行为）。多开不设上限。
    /// 叫 ShowEmpty 而不用 Show：后者签名会撞上 <see cref="Avalonia.Controls.Window.Show"/>（实例方法）
    /// </summary>
    public static void ShowEmpty()
    {
        UIManager.ShowWindow<TextFileWindow>(w => _ = w.NewEmptyAsync(), isMulti: true);
    }

    /// <summary>
    /// 只读查看一段文本：工具结果、旁白全文、超编辑上限的文件。
    /// 刻意允许多开（参数与结果、两次调用的结果都得能并排对着看）；复用缓存窗时只拿只读窗，
    /// 藏起来的编辑窗留给轮盘「上次的编辑器」
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="text">正文</param>
    /// <param name="sourceName">语言来源：决定高亮、行号与默认预览的文件名或路径；为空不高亮</param>
    /// <param name="preview">是否默认进 Markdown 预览；不给就看来源是不是 Markdown 文件</param>
    /// <param name="firstLineNumber">首行在原文件里的行号（Read 从中间读起的片段）</param>
    public static void ShowText(string title, string text, string? sourceName = null, bool? preview = null,
        int firstLineNumber = 1)
    {
        UIManager.ShowWindow<TextFileWindow>(w => w.SetTextSource(title, text, sourceName, preview, firstLineNumber),
            isMulti: true, canReuse: w => w._document.IsReadOnly);
    }

    /// <summary>
    /// 只读跟读一段边生成边长的正文（流式的思考过程）：默认按 Markdown 渲染，打开时停在最新处
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="source">正文来源</param>
    public static void ShowLive(string title, ILiveText source)
    {
        UIManager.ShowWindow<TextFileWindow>(w => w.SetLiveSource(title, source),
            isMulti: true, canReuse: w => w._document.IsReadOnly);
    }

    /// <summary>
    /// 轮盘入口：无可见窗时只把缓存的隐藏窗抬上来、不调 <see cref="NewEmptyAsync"/> 清内容；
    /// 有可见窗或无任何实例时走 <see cref="ShowEmpty"/>（新建空文档）。只读查看窗不算「上次的编辑器」。
    /// 刻意不用 LRU：复用 <see cref="UIManager"/> 现有的"第一个隐藏窗"顺序，随机复用可接受，
    /// 省一个静态关闭栈的维护成本。
    /// </summary>
    public static void ShowLastOrEmpty()
    {
        bool anyVisible = false;
        bool hasHidden = false;
        foreach (UiharuWindowBase win in UIManager.GetWindows<TextFileWindow>())
        {
            if (win is TextFileWindow { _document.IsReadOnly: true }) continue;
            if (win.IsVisible) anyVisible = true;
            else hasHidden = true;
            if (anyVisible) break;
        }

        if (!anyVisible && hasHidden)
        {
            // action 传 null：只 RequestShow，不换源不清内容
            UIManager.ShowWindow<TextFileWindow>(action: null, isMulti: false, canReuse: w => !w._document.IsReadOnly);
            return;
        }

        ShowEmpty();
    }

    /// <summary>
    /// 打开一个文件进编辑窗（分流在 <see cref="FileOpener"/> 里做，文本才到这）。
    /// 窗还没开的调用方（文件搜索、markdown 链接）请直接调 <see cref="FileOpener"/>。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="lineNumber">打开后要定位到的行号（内容搜索命中时传），其余情况为 null</param>
    public static void Show(string filePath, int? lineNumber = null)
    {
        FileOpener.Open(filePath, lineNumber);
    }

    /// <summary>
    /// 这个文件已在某扇可见的编辑窗里开着就把那扇窗抬到前面（带行号则跳过去），不再另开一扇
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="lineNumber">要定位的行号</param>
    /// <returns>找到并抬起了已开的窗口</returns>
    public static bool FocusIfOpen(string filePath, int? lineNumber = null)
    {
        foreach (UiharuWindowBase win in UIManager.GetWindows<TextFileWindow>())
        {
            if (win is not TextFileWindow { IsVisible: true } window) continue;
            if (window._document.FilePath is not { } openPath
                || !FileOpener.PathComparer.Equals(Path.GetFullPath(openPath), filePath)) continue;
            if (lineNumber is > 0) window.DocumentView.GoToLine(lineNumber.Value);
            window.RequestShow();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 已分流好的文本装载：只剩三态确认 + 换源，不再读盘。
    /// 打开对话框/拖放走 <see cref="LoadFileAsync"/>（窗已在那，只管装）。
    /// </summary>
    public async Task LoadTextAsync(string filePath, string text, Encoding encoding, bool hasBom,
        int? lineNumber = null)
    {
        if (!await ConfirmDiscardUnsavedAsync(Loc.Text(LangKey.TextFileOpenWhileDirtyConfirm))) return;

        if (!File.Exists(filePath))
        {
            MessageService.ShowNotification(Loc.Text(LangKey.TextFileFileMissing), severity: MessageSeverity.Error);
            return;
        }

        SetSource(filePath, text, encoding, hasBom, lineNumber);
    }

    /// <summary>
    /// 切到空文档。落在有未保存改动的复用窗口上时先三态确认（保存 / 不保存 / 留下）
    /// </summary>
    public async Task NewEmptyAsync()
    {
        if (!await ConfirmDiscardUnsavedAsync(Loc.Text(LangKey.TextFileOpenWhileDirtyConfirm))) return;
        LoadDocument(TextFileDocument.Untitled(), string.Empty, preview: false, lineNumber: null);
    }

    /// <summary>
    /// 异步装载文件（窗已开着的流程：打开对话框 / 拖放）。
    /// 先分流再确认：非文本（图片/二进制/超大）与读失败根本不碰当前文档——
    /// 旧流程先弹三态确认，拖张图片进来点个"否"就把未保存的修改丢了；
    /// 空白窗也不会因为一次外部打开就把自己关掉。
    /// 只有真读出了文本、要替换当前内容时，才三态确认。
    /// </summary>
    public async Task LoadFileAsync(string filePath, int? lineNumber = null)
    {
        FileOpener.RouteOutcome outcome = await FileOpener.RouteAsync(filePath);
        if (outcome.Text == null) return;

        if (!await ConfirmDiscardUnsavedAsync(Loc.Text(LangKey.TextFileOpenWhileDirtyConfirm))) return;

        SetSource(filePath, outcome.Text.Text!, outcome.Text.Encoding!, outcome.Text.HasBom, lineNumber);
    }

    private void SetSource(string filePath, string text, Encoding encoding, bool hasBom, int? lineNumber)
    {
        TextFileDocument document = TextFileDocument.ForFile(filePath, encoding, hasBom);
        LoadDocument(document, text, document.IsMarkdown, lineNumber);
        _setting.RememberFile(filePath); //装载成功才记：「最近打开」只收真打开过的
    }

    /// <summary>装载只读文本。预览、复制、另存为拿到的都是原文，不是编辑器里硬切过的副本</summary>
    internal void SetTextSource(string title, string text, string? sourceName, bool? preview, int firstLineNumber = 1)
    {
        TextFileDocument document = TextFileDocument.ReadOnly(title, sourceName, firstLineNumber);
        LoadDocument(document, text, preview ?? document.IsMarkdown, lineNumber: null);
    }

    /// <summary>跟读流式正文。同一份正文再点一次不重载，读到哪还在哪</summary>
    internal void SetLiveSource(string title, ILiveText source)
    {
        if (ReferenceEquals(_live, source)) return;
        StopLive();
        // 订阅与取全量在同一锁内：推理线程在两步之间追加也不丢段
        string text = source.Subscribe(OnLiveAppend);
        LoadDocument(TextFileDocument.ReadOnly(title, null), text,
            preview: text.Length <= TextDocumentView.PreviewRenderMaxChars, lineNumber: null);
        _live = source;
        // 读者看的是它「正在想什么」，最新内容在尾部
        DocumentView.ScrollToEnd();
    }

    /// <summary>
    /// 换一份文档。窗口可复用（<see cref="UiharuWindowBase.IsCacheWindow"/>），换源时先退订上一份流式正文，
    /// 滚动位置与菜单状态都重来，否则新内容停在上一份的位置上
    /// </summary>
    private void LoadDocument(TextFileDocument document, string text, bool preview, int? lineNumber)
    {
        StopLive();
        _suppressDirty = true;
        _document = document;
        DocumentView.LinkBaseDirectory = document.LinkBaseDirectory;
        DocumentView.SetRawVisible(!preview);
        ApplyGlobalSettings();
        DocumentView.Load(text, isEditable: !document.IsReadOnly, preview, lineNumber);
        _suppressDirty = false;
        ResetDiskTracking();

        UpdateTitle();
        RefreshDirtyState();
        RefreshMenuStates();
        UpdateStatusBar();
    }

    // 订阅回调在界面线程触发（StreamFlushPump 的节拍）
    private void OnLiveAppend(string delta)
    {
        DocumentView.AppendText(delta);
        UpdateStatusBar();
    }

    private void StopLive()
    {
        if (_live == null) return;
        _live.Unsubscribe(OnLiveAppend);
        _live = null;
    }

    /// <summary>
    /// 当前文件那一类的行号偏好：代码要行号，Markdown、纯文本这类正文多半不要，两类各记一份
    /// </summary>
    private bool LineNumbersPreference
    {
        get => _document.IsProse ? _setting.TextLineNumbers : _setting.CodeLineNumbers;
        set
        {
            if (_document.IsProse) _setting.TextLineNumbers = value;
            else _setting.CodeLineNumbers = value;
        }
    }

    /// <summary>把全局偏好应用到当前窗口（打开时与设置变化时）</summary>
    private void ApplyGlobalSettings()
    {
        LongTextView view = DocumentView.TextView;
        view.WordWrap = _setting.WordWrap;
        WordWrapButton.IsChecked = _setting.WordWrap;
        view.ShowLineNumbers = LineNumbersPreference;
        view.FirstLineNumber = _document.FirstLineNumber;
        LineNumbersMenuItem.IsChecked = LineNumbersPreference;
        SyntaxMenuItem.IsChecked = _setting.SyntaxHighlight;
        view.SyntaxSourceName = _setting.SyntaxHighlight ? _document.LanguageSource : null;
    }

    /// <summary>别的窗口改了全局偏好，本窗口（缓存复用）跟着变</summary>
    private void OnGlobalSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextFileSettingConfig.WordWrap)
            or nameof(TextFileSettingConfig.CodeLineNumbers)
            or nameof(TextFileSettingConfig.TextLineNumbers)
            or nameof(TextFileSettingConfig.SyntaxHighlight))
        {
            ApplyGlobalSettings();
        }
    }

    private void UpdateTitle()
    {
        string name = _document.GetTitle(Loc.Text(LangKey.TextFileUntitled));
        Title = (_document.IsDirty ? "* " : "") + name; //系统任务栏那份仍用星号
        TitleRun.Text = name;
        DirtyRun.Text = _document.IsDirty ? "  ●" : string.Empty;
        ToolTip.SetTip(TitleTextBlock, _document.FilePath ?? _document.SourceName ?? name); //悬停看全路径
    }

    private void RefreshDirtyState()
    {
        UpdateTitle();
        // 空文档的保存即另存为，所以只看改没改，不再要求有路径
        if (SaveMenuItem != null) SaveMenuItem.IsEnabled = _document.IsDirty;
    }

    private void RefreshMenuStates()
    {
        UndoMenuItem.IsEnabled = DocumentView.TextView.CanUndo;
        RedoMenuItem.IsEnabled = DocumentView.TextView.CanRedo;
    }

    /// <summary>
    /// 底部状态栏：左路径（只读文本是它的来源）、右编码 / 行数 / 字符数 / 文件大小。
    /// 内存里的只读文本没有编码与大小可言，只给行数与字符数
    /// </summary>
    private void UpdateStatusBar()
    {
        if (StatusTextBlock == null) return;

        string untitled = Loc.Text(LangKey.TextFileUntitled);
        PathTextBlock.Text = _document.GetLocation(untitled);
        ToolTip.SetTip(PathTextBlock, _document.GetLocation(untitled));

        LongTextView view = DocumentView.TextView;
        // 只读档喂给编辑器的是硬切过的副本，行数按原文算
        int lines = _document.IsReadOnly
            ? (view.Text?.AsSpan().Count('\n') ?? 0) + 1
            : view.Editor.Document?.LineCount ?? 0;
        int chars = view.Text?.Length ?? 0;
        string counts = $"{lines} {Loc.Text(LangKey.TextFileStatusLines)} · {chars} {Loc.Text(LangKey.TextFileStatusChars)}";
        string? lineEnding = _document.IsReadOnly ? null : GetLineEndingName(view.Editor.Document);
        string text = _document.IsReadOnly ? counts
            : lineEnding == null ? $"{_document.EncodingDisplay} · {counts}"
            : $"{_document.EncodingDisplay} · {lineEnding} · {counts}";
        if (_document.FilePath != null)
        {
            long size = 0;
            try { size = new FileInfo(_document.FilePath).Length; } catch { }
            text += $" · {GameUtils.FormatBytes(size)}";
        }

        StatusTextBlock.Text = text;
        UpdateCaretStatus();
    }

    private void UpdateCaretStatus()
    {
        if (CaretButton == null) return;
        CaretButton.IsVisible = !_document.IsReadOnly;
        if (_document.IsReadOnly) return;
        TextArea area = DocumentView.TextView.Editor.TextArea;
        string text = Loc.Text(LangKey.TextFileStatusCaretFormat, area.Caret.Line, area.Caret.Column);
        Selection selection = area.Selection;
        if (!selection.IsEmpty)
        {
            // 起点是按下的那头、终点是光标那头，往上拖时终点在前
            int lines = Math.Abs(selection.EndPosition.Line - selection.StartPosition.Line) + 1;
            text += Loc.Text(LangKey.TextFileStatusSelectionFormat, selection.Length, lines);
        }

        CaretTextBlock.Text = text;
    }

    /// <summary>按第一行的行尾报换行符（编辑器原样保留行尾，新敲的换行也跟着文档已有的走）；只有一行时不报</summary>
    private static string? GetLineEndingName(TextDocument? document)
    {
        if (document == null || document.LineCount < 2) return null;
        DocumentLine first = document.GetLineByNumber(1);
        if (first.DelimiterLength == 2) return "CRLF";
        return document.GetCharAt(first.EndOffset) == '\r' ? "CR" : "LF";
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_document.IsDirty && !_closingPromptOpen)
        {
            // 渲染线程不能等异步确认，先拦住这次关闭，确认完再 SafeClose 走常规分支
            e.Cancel = true;
            _closingPromptOpen = true;
            _ = ConfirmCloseAsync();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        // 缓存窗口只是隐藏、不释放——跟读的源条目可能已随会话切换被丢弃，必须退订
        StopLive();
        StopWatchingDisk();
    }

    protected override void OnClosed(EventArgs e)
    {
        _setting.PropertyChanged -= OnGlobalSettingChanged;
        StopLive();
        StopWatchingDisk();
        base.OnClosed(e);
    }
}
