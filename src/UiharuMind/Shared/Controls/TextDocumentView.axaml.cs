using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 一段文本的两种看法：原文（<see cref="LongTextView"/>，可编辑或只读）与 Markdown 预览（<see cref="ChunkedMarkdownViewer"/>）。
/// 文本窗拿它装文件、工具结果与流式的思考过程；窗口只管文档与交互，看法的切换、定位与跟底都在这里。
///
/// 预览只在切过去时按原文重建（内容没变则不重建，来回切不丢已渲染的段）；流式追加时两边一起长。
/// 换了正文后的定位（回顶或某一行）在预览态下挂起，切回原文时补做。
/// </summary>
public partial class TextDocumentView : UserControl
{
    /// <summary>超过这么长的 Markdown 不做渲染预览：分段后打开不卡，但纯文本占位与滚过的段仍随长度增长</summary>
    public const int PreviewRenderMaxChars = 512 * 1024;

    private bool _pendingScrollHome; //换了正文要回顶：预览态下原文不可见，切回时补做
    private int? _pendingScrollLine; //要定位的行（内容搜索命中），同样可能挂起

    public TextDocumentView()
    {
        InitializeComponent();
    }

    /// <summary>切了预览或原文（参数为是否在预览），宿主据此同步工具栏的开关</summary>
    public event EventHandler<bool>? PreviewChanged;

    /// <summary>当前是否在看预览</summary>
    public bool IsPreview => PreviewHost.IsVisible;

    /// <summary>预览里相对链接的解析基目录</summary>
    public string? LinkBaseDirectory
    {
        get => MarkdownViewer.LinkBaseDirectory;
        set => MarkdownViewer.LinkBaseDirectory = value;
    }

    /// <summary>
    /// 换一份正文。先定能否编辑再换正文：只读档喂给编辑器前会把超长行硬切开，可编辑档原样
    /// </summary>
    /// <param name="text">正文</param>
    /// <param name="isEditable">能否编辑</param>
    /// <param name="preview">是否先看预览</param>
    /// <param name="lineNumber">要定位的行；为空回顶</param>
    public void Load(string text, bool isEditable, bool preview, int? lineNumber)
    {
        TextView.IsEditable = isEditable;
        TextView.FollowTail = false;
        TextView.Text = text;
        MarkdownViewer.MarkdownText = string.Empty; //上一份的渲染段不留，预览按需重建
        ShowPreview(preview);

        _pendingScrollHome = lineNumber is not > 0;
        _pendingScrollLine = lineNumber is > 0 ? lineNumber : null;
        // 复用的窗口换源时可能刚从隐藏恢复，立即滚动会打在无效布局上，拖到空闲时做
        Dispatcher.UIThread.Post(ApplyPendingScroll, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// 切预览或原文。切到预览时渲染最新的原文，太长的退化成纯文本档
    /// </summary>
    /// <param name="preview">是否看预览</param>
    public void ShowPreview(bool preview)
    {
        PreviewHost.IsVisible = preview;
        TextView.IsVisible = !preview;
        if (preview)
        {
            string text = TextView.Text ?? string.Empty;
            MarkdownViewer.MarkdownText = text;
            MarkdownViewer.IsPlaintext = text.Length > PreviewRenderMaxChars;
        }
        else
        {
            ApplyPendingScroll();
        }

        PreviewChanged?.Invoke(this, preview);
    }

    /// <summary>
    /// 从任一看法打开搜索。搜索在原文里做：预览态先切回原文，等布局落地再开面板，
    /// 立即开会让面板的命中区停在旧布局上（右侧按钮点了没反应）
    /// </summary>
    public void OpenSearch()
    {
        ShowPreview(false);
        Dispatcher.UIThread.Post(TextView.OpenSearch, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// 流式追加。没在看预览就只喂原文，切过去时按原文重建；
    /// 预览停在底部时跟着滚，上翻在读时不打扰（与原文视图同一口径）
    /// </summary>
    /// <param name="delta">新增的正文</param>
    public void AppendText(string delta)
    {
        bool following = PreviewHost.Offset.Y >= PreviewHost.Extent.Height - PreviewHost.Viewport.Height - 4;
        TextView.AppendText(delta);
        if (!PreviewHost.IsVisible) return;

        MarkdownViewer.AppendText(delta);
        if (following) Dispatcher.UIThread.Post(() => PreviewHost.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>两种看法都滚到底并跟住（流式正文打开时看最新的），盖掉换源时挂起的回顶</summary>
    public void ScrollToEnd()
    {
        _pendingScrollHome = false;
        _pendingScrollLine = null;
        TextView.FollowTail = true;
        TextView.ScrollToEnd();
        Dispatcher.UIThread.Post(() => PreviewHost.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void ApplyPendingScroll()
    {
        if (!TextView.IsVisible) return;
        if (_pendingScrollLine is > 0)
        {
            TextView.ScrollToLine(_pendingScrollLine.Value);
            _pendingScrollLine = null;
            _pendingScrollHome = false;
            return;
        }

        if (!_pendingScrollHome) return;
        TextView.ScrollToTop();
        _pendingScrollHome = false;
    }
}
