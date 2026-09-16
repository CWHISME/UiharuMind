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
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 文本文件窗：磁盘文本文件的编辑 + markdown 预览切换。
///
/// 与 <see cref="FullTextWindow"/>（长文只读查看）、<see cref="StringContentEditWindow"/>（会话内短文本编辑）
/// 各归各位：这里是「打开磁盘上实体文件、能保存回原路径」的那一个。
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
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
    }

    /// <summary>
    /// 打开一个文本文件进编辑窗
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="lineNumber">打开后要定位到的行号（内容搜索命中时传），其余情况为 null</param>
    public static void Show(string filePath, int? lineNumber = null)
    {
        // 窗口可复用：每次 Show 都可能落在某个之前关掉的缓存实例上，装载统一放异步里做。
        // 多开不设上限——多个文件并排对着看是 FileSearchWindow 场景的常见需求。
        UIManager.ShowWindow<TextFileWindow>(w => _ = w.LoadFileAsync(filePath, lineNumber), isMulti: true);
    }

    /// <summary>
    /// 异步装载文件。可复用窗口上如果有未保存的改动，先三态确认（保存 / 不保存 / 留下）
    /// </summary>
    public async Task LoadFileAsync(string filePath, int? lineNumber = null)
    {
        if (_dirty)
        {
            var choice = await MessageService.ConfirmWithCancelAsync(
                Loc.Text("TextFileOpenWhileDirtyConfirm"),
                Loc.Text("TextFileCloseDirtyTitle"));
            if (choice == EConfirmChoice.Cancel) return;
            if (choice == EConfirmChoice.Yes && !await TrySaveAsync()) return;
            _dirty = false; //No：放弃改动，继续打开新文件
            RefreshDirtyState();
        }

        if (!File.Exists(filePath))
        {
            MessageService.ShowNotification(Loc.Text("TextFileFileMissing"), severity: MessageSeverity.Error);
            return;
        }

        TextFileReadResult result = await TextFileCodec.ReadTextAsync(filePath, default);
        if (!result.Success || result.Text == null || result.Encoding == null)
        {
            MessageService.ShowNotification(
                $"{Loc.Text("TextFileOpenFailed")} ({result.ErrorCode})", severity: MessageSeverity.Error);
            return;
        }

        SetSource(filePath, result.Text, result.Encoding, result.HasBom, lineNumber);
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
        if (_filePath == null) return false;

        try
        {
            await TextFileCodec.WriteTextAsync(
                _filePath, TextView.Text ?? string.Empty, _encoding, _hasBom, default);
            _dirty = false;
            RefreshDirtyState();
            RefreshMenuStates();
            MessageService.ShowNotification(Loc.Text("TextFileSaved"), severity: MessageSeverity.Success);
            return true;
        }
        catch (Exception e)
        {
            MessageService.ShowNotification(
                $"{Loc.Text("TextFileSaveFailed")} {e.Message}", severity: MessageSeverity.Error);
            return false;
        }
    }

    private void UpdateTitle()
    {
        string? fileName = _filePath == null ? null : Path.GetFileName(_filePath);
        string title = (_dirty ? "* " : "") + (fileName ?? "TextFileWindow");
        Title = title;
        TitleTextBlock.Text = title;
    }

    private void RefreshDirtyState()
    {
        UpdateTitle();
        if (SaveMenuItem != null) SaveMenuItem.IsEnabled = _dirty && _filePath != null;
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

    /// <summary>底部状态栏：编码 / 行数 / 字符数 / 文件大小</summary>
    private void UpdateStatusBar()
    {
        if (StatusTextBlock == null || _filePath == null) return;

        long size = 0;
        try { size = new FileInfo(_filePath).Length; } catch { }
        int lines = TextView.Editor.Document?.LineCount ?? 0;
        int chars = TextView.Text?.Length ?? 0;
        StatusTextBlock.Text =
            $"{FormatEncodingDisplay()} · {lines} {Loc.Text("TextFileStatusLines")} · {chars} {Loc.Text("TextFileStatusChars")} · {GameUtils.FormatBytes(size)}";
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
        // macOS 的搜索习惯是 Cmd+F（KeyModifiers.Meta），所以 Ctrl 和 Meta 都认
        bool searchShortcut = e.Key == Key.F
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (searchShortcut)
        {
            OpenSearchFromAnywhere();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
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
                Loc.Text("TextFileCloseDirtyConfirm"),
                Loc.Text("TextFileCloseDirtyTitle"));
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

    private async void SaveAsMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_filePath == null) return;
        var uri = await App.FilesService.SaveFileAsync(this, Path.GetFileName(_filePath));
        if (uri == null) return; //用户取消

        try
        {
            await TextFileCodec.WriteTextAsync(uri.LocalPath, TextView.Text ?? string.Empty, _encoding, _hasBom, default);
        }
        catch (Exception ex)
        {
            MessageService.ShowNotification(
                $"{Loc.Text("TextFileSaveFailed")} {ex.Message}", severity: MessageSeverity.Error);
            return;
        }

        _filePath = uri.LocalPath;
        _dirty = false;
        RefreshDirtyState();
        RefreshMenuStates();
        MessageService.ShowNotification(Loc.Text("TextFileSaved"), severity: MessageSeverity.Success);
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