using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Features.ScreenCapture.Ocr;

/// <summary>
/// OCR 文字选择层：把识别出的每一行摆成一块透明的 <see cref="SelectableTextBlock"/> 盖在图上，
/// 选区绘制与字形排版交给这些文本控件，<b>选择行为则整个由本层接管</b>。
///
/// 之所以不让各行自己处理指针：行块只看得见自己那一行，跨行拖动必然退化成「整行整行地选」。
/// 本层把指针位置换算成 (行号, 字符位置) 这一对全局坐标，再把区间摊回各行的
/// SelectionStart/End——首尾行按字符截断、中间行整行，这才是文本编辑器的选择语义。
///
/// 命中范围经 <see cref="ICustomHitTest"/> 收窄到文字行盒：行外的按下照旧穿到钉图窗去拖窗。
/// 不碰平台、不碰识别、不碰剪贴板（复制经 <see cref="CopyRequested"/> 外抛）。
/// </summary>
public sealed class OcrTextSelectionLayer : Panel, ICustomHitTest
{
    // 行文字先按这个字号量一次，再由 ArrangeOverride 缩放铺满行盒：
    // 选区高亮因此与识别框严丝合缝，不必去猜每行该用多大的字
    private const double MeasureFontSize = 16;

    // 行盒外的宽容量：贴着行边缘按下也算命中
    private const double HitSlack = 4;

    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0));
    private static readonly IBrush PlateBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(140, 46, 155, 255));

    private readonly MenuFlyout _flyout;
    private readonly MenuItem _copyItem;
    private readonly DimLayer _dim;
    private readonly List<SelectableTextBlock> _blocks = new();
    private readonly List<Rect> _boxes = new();

    private IReadOnlyList<OcrTextLine> _lines = Array.Empty<OcrTextLine>();
    private TextPoint? _anchor; //拖选起点，null 表示当前没在拖
    private bool _dragging;

    /// <summary>
    /// 用户要求复制选中文字（右键菜单或 Cmd/Ctrl+C）。参数为拼好的多行文本，非空。
    /// </summary>
    public event Action<string>? CopyRequested;

    public OcrTextSelectionLayer()
    {
        ClipToBounds = true;
        IsVisible = false;
        Focusable = true; //Cmd/Ctrl+C 要靠焦点收键盘事件
        Cursor = new Cursor(StandardCursorType.Ibeam);

        _dim = new DimLayer(this);
        Children.Add(_dim);

        _copyItem = new MenuItem();
        _copyItem.Click += (_, _) => RaiseCopy();
        var selectAllItem = new MenuItem();
        selectAllItem.Click += (_, _) => SelectAll();
        _flyout = new MenuFlyout { ItemsSource = new[] { _copyItem, selectAllItem } };
        _flyout.Opening += (_, _) =>
        {
            _copyItem.Header = Lang.Copy;
            selectAllItem.Header = Lang.PreviewOcr_SelectAll;
            _copyItem.IsEnabled = SelectedText.Length > 0;
        };
        // 菜单挂在层上：行块不参与命中，右键事件的 Source 就是本层，Control 会就地抛 ContextRequested
        ContextFlyout = _flyout;
    }

    /// <summary>
    /// 当前选中的文字，按行拼接；没有选中时为空串。
    /// </summary>
    public string SelectedText
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var block in _blocks)
            {
                string text = block.SelectedText;
                if (string.IsNullOrEmpty(text)) continue;
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(text);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// 换上一组识别结果；传空集合等同于 <see cref="Clear"/>。层的可见性随之开关。
    /// </summary>
    /// <param name="lines">识别出的文字行（归一化行盒）</param>
    public void SetLines(IReadOnlyList<OcrTextLine>? lines)
    {
        foreach (var block in _blocks) Children.Remove(block);
        _blocks.Clear();
        _lines = lines ?? Array.Empty<OcrTextLine>();
        _anchor = null;
        _dragging = false;

        foreach (var line in _lines)
        {
            var block = CreateLineBlock(line.Text);
            _blocks.Add(block);
            Children.Add(block);
        }

        IsVisible = _blocks.Count > 0;
        InvalidateMeasure();
    }

    /// <summary>
    /// 清空识别结果并隐藏本层。
    /// </summary>
    public void Clear() => SetLines(null);

    /// <summary>
    /// 选中全部文字。
    /// </summary>
    public void SelectAll()
    {
        if (_blocks.Count == 0) return;
        SetSelection(new TextPoint(0, 0), new TextPoint(_blocks.Count - 1, LineLength(_blocks.Count - 1)));
    }

    /// <summary>
    /// 取消所有选中。
    /// </summary>
    public void ClearSelection()
    {
        foreach (var block in _blocks) block.ClearSelection();
    }

    /// <inheritdoc />
    public bool HitTest(Point point) => HitLine(point) >= 0;

    private SelectableTextBlock CreateLineBlock(string text)
    {
        return new SelectableTextBlock
        {
            Text = text,
            FontSize = MeasureFontSize,
            Padding = new Thickness(0),
            TextWrapping = TextWrapping.NoWrap,
            // 字形由底图提供，这里只要选区
            Foreground = Brushes.Transparent,
            SelectionForegroundBrush = Brushes.Transparent,
            SelectionBrush = SelectionBrush,
            // 指针一律由本层接管：行块自带的那套只认得本行，跨行拖动会退化成整行选
            IsHitTestVisible = false
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _dim.Arrange(new Rect(finalSize));

        _boxes.Clear();
        for (int i = 0; i < _blocks.Count && i < _lines.Count; i++)
        {
            var box = _lines[i].Box;
            var rect = new Rect(box.X * finalSize.Width, box.Y * finalSize.Height,
                box.Width * finalSize.Width, box.Height * finalSize.Height);
            _boxes.Add(rect);

            var block = _blocks[i];
            var desired = block.DesiredSize;
            if (rect.Width <= 0 || rect.Height <= 0 || desired.Width <= 0 || desired.Height <= 0)
            {
                block.Arrange(default);
                continue;
            }

            // RenderTransform 不参与布局，在 Arrange 里改不会引起重排；
            // TranslatePoint 会带上它的逆变换，指针换算不受影响
            block.RenderTransformOrigin = RelativePoint.TopLeft;
            block.RenderTransform = new ScaleTransform(rect.Width / desired.Width, rect.Height / desired.Height);
            block.Arrange(new Rect(rect.X, rect.Y, desired.Width, desired.Height));
        }

        _dim.InvalidateVisual();
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;

        if (properties.IsRightButtonPressed)
        {
            // 点在已选区域内就保住选区（要复制的正是它），否则收掉重来。
            // 这里吃掉按下只是为了不让钉图窗当成拖窗；<b>松开时绝不能设 Handled</b>，
            // 否则 Control.OnPointerReleased 不抛 ContextRequested，右键菜单就出不来
            if (!IsInsideSelection(point)) ClearSelection();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;
        if (PositionAt(point) is not { } position) return;

        Focus();
        switch (e.ClickCount)
        {
            case >= 3:
                SetSelection(new TextPoint(position.Line, 0), new TextPoint(position.Line, LineLength(position.Line)));
                break;
            case 2:
                SelectWordAt(position);
                break;
            default:
                _anchor = position;
                _dragging = true;
                SetSelection(position, position);
                break;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _anchor is not { } anchor) return;

        // 抓着指针，拖出行盒甚至拖出图外也照样跟：归到最近的一行
        if (NearestPositionAt(e.GetPosition(this)) is not { } focus) return;
        SetSelection(anchor, focus);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // 基类在这里抛 ContextRequested（右键且事件未被处理），顺序不能调
        base.OnPointerReleased(e);
        _dragging = false;
        _anchor = null;
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool copyModifier = e.KeyModifiers.HasFlag(
            OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        if (copyModifier && e.Key == Key.C)
        {
            RaiseCopy();
            e.Handled = true;
        }
        else if (copyModifier && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    private void RaiseCopy()
    {
        string text = SelectedText;
        if (text.Length > 0) CopyRequested?.Invoke(text);
    }

    // 把 (行, 字符) 区间摊回各行：首尾行按字符截断，中间行整行，区间外的清空
    private void SetSelection(TextPoint a, TextPoint b)
    {
        var (start, end) = a <= b ? (a, b) : (b, a);
        for (int i = 0; i < _blocks.Count; i++)
        {
            var block = _blocks[i];
            if (i < start.Line || i > end.Line)
            {
                block.ClearSelection();
                continue;
            }

            int from = i == start.Line ? start.Offset : 0;
            int to = i == end.Line ? end.Offset : LineLength(i);
            if (from >= to)
            {
                block.ClearSelection();
                continue;
            }

            block.SelectionStart = from;
            block.SelectionEnd = to;
        }
    }

    private void SelectWordAt(TextPoint position)
    {
        string text = _lines[position.Line].Text;
        int offset = Math.Clamp(position.Offset, 0, text.Length);
        int from = offset;
        int to = offset;
        while (from > 0 && IsWordChar(text[from - 1])) from--;
        while (to < text.Length && IsWordChar(text[to])) to++;
        // 落在分隔符上（标点、空格）就退化成选中该字符，不至于什么都没选
        if (from == to && to < text.Length) to++;
        SetSelection(new TextPoint(position.Line, from), new TextPoint(position.Line, to));
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private bool IsInsideSelection(Point point)
    {
        if (PositionAt(point) is not { } position) return false;
        var block = _blocks[position.Line];
        int low = Math.Min(block.SelectionStart, block.SelectionEnd);
        int high = Math.Max(block.SelectionStart, block.SelectionEnd);
        return low != high && position.Offset >= low && position.Offset <= high;
    }

    private TextPoint? PositionAt(Point point)
    {
        int line = HitLine(point);
        return line < 0 ? null : new TextPoint(line, OffsetIn(line, point));
    }

    private TextPoint? NearestPositionAt(Point point)
    {
        int line = HitLine(point);
        if (line < 0) line = NearestLine(point);
        return line < 0 ? null : new TextPoint(line, OffsetIn(line, point));
    }

    // 指针 → 该行内的字符位置。TranslatePoint 会把行块的缩放一并逆掉，
    // 拿到的是排版坐标，直接喂 TextLayout
    private int OffsetIn(int line, Point point)
    {
        var block = _blocks[line];
        var local = this.TranslatePoint(point, block) ?? default;
        double x = Math.Clamp(local.X, 0, Math.Max(0, block.Bounds.Width));
        double y = Math.Clamp(local.Y, 0, Math.Max(0, block.Bounds.Height));
        var hit = block.TextLayout.HitTestPoint(new Point(x, y));
        // TextPosition 指的是命中字符的起点，落在字符右半边时要算到它后面去，
        // 否则永远选不到最后一个字
        return Math.Clamp(hit.IsTrailing ? hit.TextPosition + 1 : hit.TextPosition, 0, LineLength(line));
    }

    private int LineLength(int line) => _lines[line].Text.Length;

    // 行命中：框外放宽 HitSlack 提升手感，倒序让后来者居上
    private int HitLine(Point point)
    {
        for (int i = _boxes.Count - 1; i >= 0; i--)
        {
            if (_boxes[i].Inflate(new Thickness(HitSlack)).Contains(point)) return i;
        }

        return -1;
    }

    private int NearestLine(Point point)
    {
        double best = double.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < _boxes.Count; i++)
        {
            double distance = DistanceToRect(point, _boxes[i]);
            if (distance >= best) continue;
            best = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static double DistanceToRect(Point point, Rect rect)
    {
        double dx = Math.Max(Math.Max(rect.X - point.X, 0), point.X - rect.Right);
        double dy = Math.Max(Math.Max(rect.Y - point.Y, 0), point.Y - rect.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 阅读顺序上的一个位置：第几行的第几个字符。行号即行块列表的下标。
    /// </summary>
    private readonly record struct TextPoint(int Line, int Offset)
    {
        public static bool operator <=(TextPoint a, TextPoint b) =>
            a.Line != b.Line ? a.Line < b.Line : a.Offset <= b.Offset;

        public static bool operator >=(TextPoint a, TextPoint b) =>
            a.Line != b.Line ? a.Line > b.Line : a.Offset >= b.Offset;
    }

    /// <summary>
    /// 变暗打洞层：<see cref="Panel"/> 的 Render 是 sealed，画不了东西，只能交给一个专职子控件。
    /// 不参与命中测试，指针一律落到本层自己的 <see cref="ICustomHitTest"/> 上。
    /// </summary>
    private sealed class DimLayer : Control
    {
        private readonly OcrTextSelectionLayer _owner;

        public DimLayer(OcrTextSelectionLayer owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext context)
        {
            var boxes = _owner._boxes;
            if (boxes.Count == 0) return;

            // 能选的行露出来，其余压暗，一眼看出哪里可选
            var holes = new GeometryGroup();
            foreach (var rect in boxes) holes.Children.Add(new RectangleGeometry(rect));
            context.DrawGeometry(DimBrush, null, new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(Bounds.Size)), holes));

            foreach (var rect in boxes) context.DrawRectangle(PlateBrush, null, rect, 4, 4);
        }
    }
}
