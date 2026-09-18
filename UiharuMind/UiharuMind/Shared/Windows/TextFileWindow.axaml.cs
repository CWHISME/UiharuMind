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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 文本文件窗：磁盘文本文件的编辑 + markdown 预览切换。
///
/// 与 <see cref="FullTextWindow"/>（长文只读查看）、<see cref="StringContentEditWindow"/>（会话内短文本编辑）
/// 各归各位：这里是「打开磁盘上实体文件、能保存回原路径」的那一个。
/// 无路径也能开（空文档、标题显示未命名），文件从「文件 / 打开」菜单或直接拖进来。
///
/// 编辑态（<see cref="Controls.LongTextView"/> 的 IsEditable 档）与预览态
/// （<see cref="Controls.SimpleMarkdownViewer"/>）叠在同一片区域，切模式只改可见性——
/// 编辑 Document 不销毁，撤销栈与滚动位置跨预览切换保留（换 Document 会清撤销栈，见 LongTextView.ApplyText）。
///
/// 保存走 <see cref="TextFileCodec"/>：原编码、原 BOM 写回，行尾不归一。
/// 关闭前未保存有三态确认（保存 / 不保存 / 取消，见 <see cref="EConfirmChoice"/>）。
/// </summary>
public partial class TextFileWindow : QuickWindowBase
{
    private string? _filePath;
    private Encoding _encoding = Encoding.UTF8;
    private bool _hasBom;
    private bool _dirty;
    private bool _suppressDirty; //SetSource 期间不把程序化设置当用户编辑
    private bool _closingPromptOpen; //防止 OnClosing 里弹确认被重复触发
    private readonly TextFileSettingConfig _setting = TextFileSettingConfig.Current;
    private bool _pendingScrollHome; //新文件回顶在预览态下挂起，切回编辑时补做
    private int? _pendingScrollLine; //内容搜索命中的行定位，同样可能挂起

    private IMessageService MessageService => App.Services.GetRequiredService<IMessageService>();

    public TextFileWindow()
    {
        InitializeComponent();
        TextView.TextChanged += (_, _) =>
        {
            if (_suppressDirty) return;
            _dirty = true;
            RefreshDirtyState();
            RefreshMenuStates();
            UpdateStatusBar();
        };
        // 全局偏好：别的文本窗改了设置，这个还开着的窗口跟着变。
        // 缓存窗口与设置单例同生命周期，不主动退订（复用时还要继续联动）
        _setting.PropertyChanged += OnGlobalSettingChanged;

        // 整窗接受文件拖放（对照 ConversationView）：DragOver 只在文件拖放时介入，
        // 高亮蒙版盖顶但不拦事件，可见性由下面三个 handler 控制
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
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
    /// 轮盘入口：无可见窗时只把缓存的隐藏窗抬上来、不调 <see cref="NewEmptyAsync"/> 清内容；
    /// 有可见窗或无任何实例时走 <see cref="ShowEmpty"/>（新建空文档）。
    /// 刻意不用 LRU：复用 <see cref="UIManager"/> 现有的"第一个隐藏窗"顺序，随机复用可接受，
    /// 省一个静态关闭栈的维护成本。
    /// </summary>
    public static void ShowLastOrEmpty()
    {
        bool anyVisible = false;
        bool hasHidden = false;
        foreach (UiharuWindowBase win in UIManager.GetWindows<TextFileWindow>())
        {
            if (win.IsVisible) anyVisible = true;
            else hasHidden = true;
            if (anyVisible) break;
        }

        if (!anyVisible && hasHidden)
        {
            // action 传 null：只 RequestShow，不换源不清内容
            UIManager.ShowWindow<TextFileWindow>(action: null, isMulti: false);
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
        SetEmptySource();
    }

    /// <summary>
    /// 有未保存改动时三态确认，返回是否可以继续（打开新文件 / 新建空文档共用）
    /// </summary>
    /// <returns>无改动、或用户选择保存/放弃后返回 true；取消（或保存失败）返回 false</returns>
    private async Task<bool> ConfirmDiscardUnsavedAsync(string message)
    {
        if (!_dirty) return true;
        var choice = await MessageService.ConfirmWithCancelAsync(message, Loc.Text(LangKey.TextFileCloseDirtyTitle));
        if (choice == EConfirmChoice.Cancel) return false;
        if (choice == EConfirmChoice.Yes && !await TrySaveAsync()) return false;
        _dirty = false; //No：放弃改动
        RefreshDirtyState();
        return true;
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

    /// <summary>
    /// 装载正文。窗口可复用（<see cref="UiharuWindowBase.IsCacheWindow"/>），换源时把
    /// 滚动位置显式复位、菜单状态重算，否则新内容停在上一份的位置上
    /// </summary>
    private void SetSource(string filePath, string text, Encoding encoding, bool hasBom, int? lineNumber)
    {
        _suppressDirty = true;
        _filePath = filePath;
        _encoding = encoding;
        _hasBom = hasBom;
        _dirty = false;

        TextView.Text = text;
        MarkdownViewer.LinkBaseDirectory = Path.GetDirectoryName(filePath); //预览里相对链接基于文件目录解析

        ApplyGlobalSettings(); //换行/行号/语法高亮按全局偏好

        bool isMarkdown = IsMarkdownFile(filePath);
        SyncPreviewVisibility(isMarkdown);

        // 复用窗口换源时可能刚从 Hide 恢复，立即回顶会打在无效布局上（滚不动）。
        // 拖到下一帧；预览态下（编辑器不可见）等切回编辑再补
        _pendingScrollHome = lineNumber is not > 0;
        _pendingScrollLine = lineNumber is > 0 ? lineNumber : null;
        Dispatcher.UIThread.Post(ApplyInitialScroll, DispatcherPriority.ApplicationIdle);
        _suppressDirty = false;

        _setting.RememberFile(filePath); //装载成功才记：「最近打开」只收真打开过的

        UpdateTitle();
        RefreshDirtyState();
        RefreshMenuStates();
        UpdateStatusBar();
    }

    /// <summary>
    /// 切到空文档：路径置空、编码回到默认、无路径不高亮（扩展名未知），预览回到编辑态
    /// </summary>
    private void SetEmptySource()
    {
        _suppressDirty = true;
        _filePath = null;
        _encoding = Encoding.UTF8;
        _hasBom = false;
        _dirty = false;

        TextView.Text = string.Empty;
        TextView.Text = string.Empty;
        MarkdownViewer.LinkBaseDirectory = null;
        MarkdownViewer.MarkdownText = string.Empty;

        ApplyGlobalSettings(); //SyntaxSourceName 会因 _filePath 为 null 而关掉高亮
        SyncPreviewVisibility(false);

        _pendingScrollHome = true;
        _pendingScrollLine = null;
        Dispatcher.UIThread.Post(ApplyInitialScroll, DispatcherPriority.ApplicationIdle);
        _suppressDirty = false;

        UpdateTitle();
        RefreshDirtyState();
        RefreshMenuStates();
        UpdateStatusBar();
    }

    private static bool IsMarkdownFile(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TrySaveAsync()
    {
        // 空文档没有回写路径，保存即另存为（普通编辑器行为）
        if (_filePath == null) return await SaveAsAsync();

        try
        {
            await TextFileCodec.WriteTextAsync(
                _filePath, TextView.Text ?? string.Empty, _encoding, _hasBom, default);
            _dirty = false;
            RefreshDirtyState();
            RefreshMenuStates();
            MessageService.ShowNotification(Loc.Text(LangKey.TextFileSaved), severity: MessageSeverity.Success);
            return true;
        }
        catch (Exception e)
        {
            MessageService.ShowNotification(
                $"{Loc.Text(LangKey.TextFileSaveFailed)} {e.Message}", severity: MessageSeverity.Error);
            return false;
        }
    }

    /// <summary>另存为：空文档也能调，存完路径落定、标题与高亮跟着新扩展名走</summary>
    /// <returns>存成功返回 true；用户取消或失败返回 false</returns>
    private async Task<bool> SaveAsAsync()
    {
        string defaultName = _filePath == null ? "untitled.txt" : Path.GetFileName(_filePath);
        var uri = await App.FilesService.SaveFileAsync(this, defaultName);
        if (uri == null) return false; //用户取消

        try
        {
            await TextFileCodec.WriteTextAsync(uri.LocalPath, TextView.Text ?? string.Empty, _encoding, _hasBom, default);
        }
        catch (Exception ex)
        {
            MessageService.ShowNotification(
                $"{Loc.Text(LangKey.TextFileSaveFailed)} {ex.Message}", severity: MessageSeverity.Error);
            return false;
        }

        _filePath = uri.LocalPath;
        _dirty = false;
        _setting.RememberFile(_filePath); //落定新路径，记一笔
        ApplyGlobalSettings(); //扩展名可能变了，高亮按新路径重算
        RefreshDirtyState();
        RefreshMenuStates();
        UpdateStatusBar();
        MessageService.ShowNotification(Loc.Text(LangKey.TextFileSaved), severity: MessageSeverity.Success);
        return true;
    }

    private void UpdateTitle()
    {
        string? fileName = _filePath == null ? null : Path.GetFileName(_filePath);
        string title = (_dirty ? "* " : "") + (fileName ?? Loc.Text(LangKey.TextFileUntitled));
        Title = title;
        TitleTextBlock.Text = title;
        ToolTip.SetTip(TitleTextBlock, _filePath ?? title); //悬停看全路径
    }

    private void RefreshDirtyState()
    {
        UpdateTitle();
        // 空文档的保存即另存为，所以只看 _dirty，不再要求 _filePath 非空
        if (SaveMenuItem != null) SaveMenuItem.IsEnabled = _dirty;
    }

    private void RefreshMenuStates()
    {
        UndoMenuItem.IsEnabled = TextView.CanUndo;
        RedoMenuItem.IsEnabled = TextView.CanRedo;
    }

    /// <summary>把全局偏好应用到当前窗口（打开时与设置变化时）</summary>
    private void ApplyGlobalSettings()
    {
        TextView.WordWrap = _setting.WordWrap;
        WordWrapButton.IsChecked = _setting.WordWrap;
        TextView.ShowLineNumbers = _setting.ShowLineNumbers;
        LineNumbersMenuItem.IsChecked = _setting.ShowLineNumbers;
        SyntaxMenuItem.IsChecked = _setting.SyntaxHighlight;
        TextView.SyntaxSourceName = _setting.SyntaxHighlight ? _filePath : null;
    }

    /// <summary>别的窗口改了全局偏好，本窗口（缓存复用）跟着变</summary>
    private void OnGlobalSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextFileSettingConfig.WordWrap))
        {
            TextView.WordWrap = _setting.WordWrap;
        }
        else if (e.PropertyName == nameof(TextFileSettingConfig.ShowLineNumbers))
        {
            TextView.ShowLineNumbers = _setting.ShowLineNumbers;
            LineNumbersMenuItem.IsChecked = _setting.ShowLineNumbers;
        }
        else if (e.PropertyName == nameof(TextFileSettingConfig.SyntaxHighlight))
        {
            SyntaxMenuItem.IsChecked = _setting.SyntaxHighlight;
            TextView.SyntaxSourceName = _setting.SyntaxHighlight ? _filePath : null;
        }
    }

    /// <summary>底部状态栏：左路径、右编码 / 行数 / 字符数 / 文件大小（空文档无路径时不显示大小）</summary>
    private void UpdateStatusBar()
    {
        if (StatusTextBlock == null) return;

        if (PathTextBlock != null)
        {
            PathTextBlock.Text = _filePath ?? Loc.Text(LangKey.TextFileUntitled);
            ToolTip.SetTip(PathTextBlock, _filePath ?? Loc.Text(LangKey.TextFileUntitled));
        }

        int lines = TextView.Editor.Document?.LineCount ?? 0;
        int chars = TextView.Text?.Length ?? 0;
        string text =
            $"{FormatEncodingDisplay()} · {lines} {Loc.Text(LangKey.TextFileStatusLines)} · {chars} {Loc.Text(LangKey.TextFileStatusChars)}";
        if (_filePath != null)
        {
            long size = 0;
            try { size = new FileInfo(_filePath).Length; } catch { }
            text += $" · {GameUtils.FormatBytes(size)}";
        }
        StatusTextBlock.Text = text;
    }

    /// <summary>编码名按惯例显示：utf-8 → UTF-8、gb18030 → GB18030，而不是原始小写 WebName</summary>
    private string FormatEncodingDisplay()
    {
        string display = _encoding.WebName switch
        {
            "utf-8" => "UTF-8",
            "utf-16" => "UTF-16",
            "utf-32" => "UTF-32",
            "gb18030" => "GB18030",
            "big5" => "Big5",
            "shift_jis" => "Shift_JIS",
            _ => _encoding.WebName
        };
        return _hasBom ? $"{display} (BOM)" : display;
    }

    /// <summary>换源后的初始滚动：回顶或定位到命中行。预览态下编辑器不可见，等切回编辑补做</summary>
    private void ApplyInitialScroll()
    {
        if (!TextView.IsVisible) return;
        if (_pendingScrollLine is > 0)
        {
            TextView.ScrollToLine(_pendingScrollLine.Value);
            _pendingScrollLine = null;
            _pendingScrollHome = false;
            return;
        }
        if (_pendingScrollHome)
        {
            TextView.ScrollToTop();
            _pendingScrollHome = false;
        }
    }

    private void WordWrapButton_Click(object? sender, RoutedEventArgs e)
    {
        _setting.WordWrap = WordWrapButton.IsChecked == true;
        TextView.WordWrap = _setting.WordWrap;
    }

    /// <summary>超过这个字符数的 markdown 不做渲染预览：LiveMarkdown 全量建视觉树太贵，
    /// 打开时那一帧会直接卡死。超限退化成纯文本档</summary>
    private const int PreviewRenderMaxChars = 512 * 1024;

    private void SyncPreviewVisibility(bool preview, bool renderNow = false)
    {
        PreviewHost.IsVisible = preview;
        TextView.IsVisible = !preview;
        PreviewButton.IsChecked = preview;
        if (!preview)
        {
            ApplyInitialScroll(); //预览态打开的新文件，切回编辑时补回顶/定位
            return;
        }

        MarkdownViewer.MarkdownText = TextView.Text ?? string.Empty; //渲染最新编辑

        // 大 markdown 不渲染，保持纯文本档
        bool tooLarge = (TextView.Text?.Length ?? 0) > PreviewRenderMaxChars;
        MarkdownViewer.IsPlaintext = tooLarge;
        if (tooLarge) return;

        // 构造器把 IsPlaintext 置成 true（纯文本档），XAML 字面量会被它覆盖，必须在代码里关掉。
        // 打开时（SetSource）不 RealizeNow：让控件自己的视口队列分帧渲染，首帧先出纯文本占位，
        // 避免 LiveMarkdown 全量解析把打开那一帧卡死；用户主动点预览按钮时 renderNow=true 立即渲染。
        MarkdownViewer.IsPlaintext = false;
        if (renderNow) MarkdownViewer.RealizeNow();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+F / Cmd+F：编辑模式下编辑器自己的 SearchPanel 先处理（Handled 后到不了这里）；
        // 到这说明是预览态或编辑器没接住——切回编辑再开搜索。
        // macOS 的搜索/打开/保存习惯是 Cmd（KeyModifiers.Meta），所以 Ctrl 和 Meta 都认
        var cmd = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta);
        if (e.Key == Key.F && cmd != 0)
        {
            OpenSearchFromAnywhere();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.O && cmd != 0)
        {
            _ = OpenFileAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.S && cmd != 0)
        {
            _ = TrySaveAsync();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>整窗拖放（对照 ConversationView）：只在文件拖放时介入并亮起蒙版</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool isFile = e.DataTransfer.Formats.Any(f => f == DataFormat.File);
        DropOverlay.IsVisible = isFile;
        if (!isFile) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave 是冒泡路由事件：指针在窗内跨过子控件边界也会冒上来，
        // 只有真的离开整个窗口才收起蒙版，否则内部移动会反复闪烁
        Point p = e.GetPosition(this);
        if (p.X < 0 || p.Y < 0 || p.X > Bounds.Width || p.Y > Bounds.Height)
            DropOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (!e.DataTransfer.Formats.Any(f => f == DataFormat.File)) return;

        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetRaw(DataFormat.File) is not IStorageItem storageItem) continue;
            string? path = storageItem.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;

            // 一次只开一个：编辑窗一份只装一份文档。二进制/超大由 LoadFileAsync 转交系统
            await LoadFileAsync(path);
            return;
        }

        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_dirty && !_closingPromptOpen)
        {
            // 渲染线程不能等异步确认，先拦住这次关闭，确认完再 SafeClose 走常规分支
            e.Cancel = true;
            _closingPromptOpen = true;
            _ = ConfirmCloseAsync();
            return;
        }

        base.OnClosing(e);
    }

    private async Task ConfirmCloseAsync()
    {
        try
        {
            var choice = await MessageService.ConfirmWithCancelAsync(
                Loc.Text(LangKey.TextFileCloseDirtyConfirm),
                Loc.Text(LangKey.TextFileCloseDirtyTitle));
            if (choice == EConfirmChoice.Cancel) return;
            if (choice == EConfirmChoice.Yes)
            {
                if (!await TrySaveAsync()) return; //保存失败留在原地，别丢数据
                SafeClose();
                return;
            }

            //No：放弃改动
            _dirty = false;
            RefreshDirtyState();
            SafeClose();
        }
        finally
        {
            _closingPromptOpen = false;
        }
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    private void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        TextView.CopyAll();
    }

    private void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenSearchFromAnywhere();
    }

    /// <summary>
    /// 从任意模式打开搜索：预览态先切回编辑。切可见性后布局要下一帧才稳定，
    /// 立即开面板会让 SearchPanel 的命中区域停在旧布局上——右侧按钮点了没反应。
    /// 等布局落地（ApplicationIdle，渲染之后）再开面板。
    /// </summary>
    private void OpenSearchFromAnywhere()
    {
        SyncPreviewVisibility(false);
        Dispatcher.UIThread.Post(TextView.OpenSearch, DispatcherPriority.ApplicationIdle);
    }

    private async void OpenMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await OpenFileAsync();
    }

    /// <summary>
    /// 「文件」菜单每次展开时重建「最近打开」（顺手剔除已删除的文件）。
    /// 入口挂在文件菜单上而不是最近子菜单上：空子菜单根本打不开，
    /// SubmenuOpened 永不触发——上次挂错地方，菜单里永远只有光杆标题。
    /// 动态条目不用 loc 绑定：语言切换后下次展开即按新语言重建
    /// </summary>
    private void FileMenuItem_SubmenuOpened(object? sender, RoutedEventArgs e)
    {
        _setting.PruneMissingFiles();
        RecentMenuItem.Items.Clear();

        if (_setting.RecentFiles.Count == 0)
        {
            RecentMenuItem.Items.Add(new MenuItem
            {
                Header = Loc.Text(LangKey.TextFileMenuRecentEmpty),
                IsEnabled = false
            });
            return;
        }

        foreach (string path in _setting.RecentFiles)
        {
            var item = new MenuItem { Header = Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            string captured = path;
            item.Click += async (_, _) => await LoadFileAsync(captured);
            RecentMenuItem.Items.Add(item);
        }

        RecentMenuItem.Items.Add(new Separator());
        var clear = new MenuItem { Header = Loc.Text(LangKey.TextFileMenuRecentClear) };
        clear.Click += (_, _) => _setting.ClearRecentFiles();
        RecentMenuItem.Items.Add(clear);
    }

    private async void NewMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await NewEmptyAsync();
    }

    /// <summary>文件 / 打开：不过滤后缀（任意文件可选），准入由 LoadFileAsync 统一判定</summary>
    private async Task OpenFileAsync()
    {
        var file = await App.FilesService.OpenFileAsync(this, null, "*");
        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return; //用户取消

        await LoadFileAsync(path);
    }

    private async void SaveAsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await SaveAsAsync();
    }

    private async void SaveMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        await TrySaveAsync();
    }

    private void UndoMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        TextView.Undo();
    }

    private void RedoMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        TextView.Redo();
    }

    private void PreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        // 用户主动点预览：立即渲染，不等视口队列
        SyncPreviewVisibility(PreviewButton.IsChecked == true, renderNow: true);
    }

    private void LineNumbersMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        _setting.ShowLineNumbers = LineNumbersMenuItem.IsChecked;
        TextView.ShowLineNumbers = _setting.ShowLineNumbers;
    }

    private void SyntaxMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        // null 表示不高亮；重新打开高亮时能恢复（TextMate 认不出扩展名也会自然退化成纯文本）
        _setting.SyntaxHighlight = SyntaxMenuItem.IsChecked;
        TextView.SyntaxSourceName = _setting.SyntaxHighlight ? _filePath : null;
    }
}