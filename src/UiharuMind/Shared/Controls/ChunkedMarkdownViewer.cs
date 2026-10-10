using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveMarkdown.Avalonia;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 长文档的 markdown 预览。整篇交给一个渲染器会一次建出整棵视觉树——100K 字实测要两三秒，
/// 成本几乎全在 Avalonia 给每个控件挂树、套样式上，解析只占几十毫秒。
///
/// 这里按顶层块切成若干段（<see cref="MarkdownChunker"/>），每段一个 <see cref="SimpleMarkdownViewer"/>，
/// 借它「进视口才渲染、每帧只放行一个」的机制：打开时只建看得见的那一两段。
/// 离视口远的段连纯文本也不排，只按字数估高占位（<see cref="MarkdownChunkSlot"/>）。
///
/// 各段的渲染器靠容器上的 <see cref="MarkdownTextBlock.IsSelectionScopeProperty"/> 拼成一个选区（库取最外层的作用域），
/// 跨段拖选与复制照常衔接。库只在按下时取一次块快照，拖动中途新渲染出来的段由
/// <see cref="MarkdownSelectionSnapshotShim"/> 补进去。
/// </summary>
public class ChunkedMarkdownViewer : UserControl
{
    private const int ChunkChars = 4 * 1024; //一段的目标字数，约一两屏

    private readonly StackPanel _panel = new();
    private string _markdownText = string.Empty;
    private string _tailText = string.Empty; //最后一段的原文：流式追加只喂它，长到两倍段长再重新切
    private bool _isPlaintext = true;
    private string? _linkBaseDirectory;
    private bool _isWatchingDrag; //按下到松开期间盯着布局，有新段渲染出来就刷新拖选快照
    private int _realizedCount;

    public ChunkedMarkdownViewer()
    {
        MarkdownTextBlock.SetIsSelectionScope(_panel, true);
        Content = _panel;

        // 隧道阶段先于渲染器拿到按下：此时库还没取快照，记下的段数正好对得上它
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => StopWatchingDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => StopWatchingDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// 整篇原文。与当前内容相同时不重建，来回切预览不会把已渲染的段丢掉
    /// </summary>
    public string MarkdownText
    {
        get => _markdownText;
        set
        {
            value ??= string.Empty;
            if (value == _markdownText) return;
            _markdownText = value;
            Rebuild();
        }
    }

    /// <summary>
    /// 纯文本档：所有段都不启动渲染器，语义同 <see cref="SimpleMarkdownViewer.IsPlaintext"/>
    /// </summary>
    public bool IsPlaintext
    {
        get => _isPlaintext;
        set
        {
            _isPlaintext = value;
            foreach (MarkdownChunkSlot slot in Slots()) slot.IsPlaintext = value;
        }
    }

    /// <summary>
    /// 相对链接的解析基目录，见 <see cref="SimpleMarkdownViewer.LinkBaseDirectory"/>
    /// </summary>
    public string? LinkBaseDirectory
    {
        get => _linkBaseDirectory;
        set
        {
            _linkBaseDirectory = value;
            foreach (SimpleMarkdownViewer viewer in Viewers()) viewer.LinkBaseDirectory = value;
        }
    }

    /// <summary>
    /// 流式追加（思考过程这类边生成边看的正文）。整篇重建会闪，成本还随长度二次增长；
    /// 这里增量只喂最后一段——<see cref="SimpleMarkdownViewer"/> 本就是为增量追加设计的。
    /// 最后一段长到两倍段长时把它重新切一次，前面切出来的段从此不再变
    /// </summary>
    /// <param name="delta">新增的原文</param>
    public void AppendText(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        _markdownText += delta;
        if (_panel.Children.Count == 0)
        {
            Rebuild();
            return;
        }

        _tailText += delta;
        if (_tailText.Length <= ChunkChars * 2)
        {
            MarkdownChunkSlot tail = (MarkdownChunkSlot)_panel.Children[^1];
            tail.Activate();
            tail.Viewer.AppendText(delta);
            return;
        }

        _panel.Children.RemoveAt(_panel.Children.Count - 1);
        AddChunks(_tailText);
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        AddChunks(_markdownText);
    }

    private void AddChunks(string text)
    {
        _tailText = string.Empty;
        foreach (string chunk in MarkdownChunker.Split(text, ChunkChars, MarkdownUpdateProducer.DefaultPipeline))
        {
            _panel.Children.Add(new MarkdownChunkSlot(chunk, _isPlaintext, _linkBaseDirectory));
            _tailText = chunk;
        }
    }

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _realizedCount = CountRealized();
        if (_isWatchingDrag) return;
        _isWatchingDrag = true;
        LayoutUpdated += OnLayoutUpdatedWhileDragging;
    }

    private void StopWatchingDrag()
    {
        if (!_isWatchingDrag) return;
        _isWatchingDrag = false;
        LayoutUpdated -= OnLayoutUpdatedWhileDragging;
    }

    // 新段要等布局跑完、模板长出来，文字块才齐，所以在布局之后刷新
    private void OnLayoutUpdatedWhileDragging(object? sender, EventArgs e)
    {
        int realized = CountRealized();
        if (realized == _realizedCount) return;
        _realizedCount = realized;
        MarkdownSelectionSnapshotShim.RefreshActive(_panel, Viewers().Select(v => v.Renderer));
    }

    private int CountRealized() => Viewers().Count(v => v.IsMarkdownRealized);

    private IEnumerable<MarkdownChunkSlot> Slots()
    {
        foreach (Control child in _panel.Children) yield return (MarkdownChunkSlot)child;
    }

    private IEnumerable<SimpleMarkdownViewer> Viewers() => Slots().Select(s => s.Viewer);
}
