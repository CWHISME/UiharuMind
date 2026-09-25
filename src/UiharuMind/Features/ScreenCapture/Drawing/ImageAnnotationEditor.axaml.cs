using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace UiharuMind.Features.ScreenCapture.Drawing;

/// <summary>
/// 图片标注编辑器（可复用控件）：画布固定为原始位图像素尺寸，
/// 缩放/平移走 RenderTransform，绘制坐标经 GetPosition 反算后天然是原始像素——
/// 保存渲染零换算。撤销/重做走命令栈（几何数据 + 控件配对）。
/// 宿主可以是独立编辑窗，也可以是将来贴图窗的编辑模式。
/// </summary>
public partial class ImageAnnotationEditor : UserControl
{
    private const double DefaultStrokeThickness = 1.5;
    private const double DefaultFontSize = 16;
    private const double MinViewScale = 0.05;
    private const double MaxViewScale = 32;
    private const double ScaleStep = 1.15;

    private Bitmap? _source;
    private double _viewScale = 1.0;
    private Point _viewOffset;

    private DrawingTool _currentTool = DrawingTool.Rectangle;
    private Color _currentColor = Colors.Red;
    private double _strokeThickness = DefaultStrokeThickness;
    private double _fontSize = DefaultFontSize;

    private readonly Stack<UndoEntry> _undoStack = new();
    private readonly Stack<UndoEntry> _redoStack = new();

    private IDrawCommand? _activeCommand;
    private Control? _activeControl;
    private bool _isDrawing;
    private Point _startPoint;
    private Point _currentPoint;

    private TextBox? _activeTextBox;

    private bool _panning;
    private Point _panStartView;
    private Point _panStartOffset;

    /// <summary>撤销/重做可用性变化（宿主刷新按钮）</summary>
    public event Action? UndoRedoChanged;

    /// <summary>视图缩放/平移变化（宿主可显示缩放比例）</summary>
    public event Action? ViewChanged;

    /// <summary>当前视图缩放倍数（原始尺寸 → 显示尺寸）</summary>
    public double ViewScale => _viewScale;

    /// <summary>
    /// 是否允许滚轮缩放视图。编辑窗关掉它：初始缩放来自贴图窗（DisplayScale），
    /// 编辑过程视图固定，画布坐标与视图比例不再变动。
    /// </summary>
    public bool AllowViewZoom { get; set; } = true;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public ImageAnnotationEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 装载一张图并重置命令栈。<b>本控件不接管位图所有权</b>（由宿主决定释放时机）。
    /// </summary>
    /// <param name="source">原始位图（96 DPI，尺寸即像素尺寸）</param>
    /// <param name="initialScale">初始视图缩放</param>
    public void SetSource(Bitmap source, double initialScale)
    {
        _source = source;
        double w = source.PixelSize.Width;
        double h = source.PixelSize.Height;
        DrawingCanvas.Width = w;
        DrawingCanvas.Height = h;
        ImageContent.Source = source;
        ImageContent.Width = w;
        ImageContent.Height = h;

        ClearCommands();
        // 左上对齐（图内容 = 编辑器原点），不做自动居中——与贴图窗同构：
        // 贴图窗图永远从窗口原点 + ShadowMargin 开始，居中会让小图比贴图时偏右下
        _viewScale = ClampScale(initialScale);
        _viewOffset = default;
        ApplyViewTransform();
        ViewChanged?.Invoke();
        UndoRedoChanged?.Invoke();
    }

    /// <summary>
    /// 清空位图引用与命令栈（宿主退出编辑模式时调用）。不释放位图——所有权在宿主。
    /// </summary>
    public void ClearSource()
    {
        _source = null;
        _viewScale = 1.0;
        _viewOffset = default;
        ClearCommands();
        ImageContent.Source = null;
        ApplyViewTransform();
        UndoRedoChanged?.Invoke();
    }

    /// <summary>换工具（窗口工具条按钮）</summary>
    public void SetTool(DrawingTool tool) => _currentTool = tool;

    /// <summary>换颜色（窗口工具条取色器）</summary>
    public void SetColor(Color color) => _currentColor = color;

    /// <summary>线宽（原始像素单位，视图缩放所见即所得）</summary>
    public void SetStrokeThickness(double thickness) => _strokeThickness = thickness;

    /// <summary>字号（原始像素单位）</summary>
    public void SetFontSize(double fontSize) => _fontSize = fontSize;

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        UndoEntry entry = _undoStack.Pop();
        DrawingCanvas.Children.Remove(entry.Control);
        _redoStack.Push(entry);
        UndoRedoChanged?.Invoke();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        UndoEntry entry = _redoStack.Pop();
        DrawingCanvas.Children.Add(entry.Control);
        _undoStack.Push(entry);
        UndoRedoChanged?.Invoke();
    }

    /// <summary>
    /// 导出合成图：命令栈重放到独立画布（无视图变换），按原始像素尺寸渲染。
    /// 不触碰显示画布，输出 = 原始位图 + 全部标注。
    /// </summary>
    public Bitmap RenderToBitmap()
    {
        if (_source == null) throw new InvalidOperationException("未装载图片");

        // 输入中的文字先落栈再渲染：保存可能由别的窗口（非激活的停靠工具条）触发，
        // 键盘焦点不会离开输入框，等 LostFocus 提交就丢了
        CommitActiveText();

        var pixelSize = _source.PixelSize;
        var renderCanvas = new Canvas { Width = pixelSize.Width, Height = pixelSize.Height };
        renderCanvas.Children.Add(new Image
        {
            Source = _source,
            Width = pixelSize.Width,
            Height = pixelSize.Height,
            Stretch = Stretch.None
        });
        foreach (UndoEntry entry in _undoStack)
        {
            renderCanvas.Children.Add(entry.Command.CreateControl());
        }

        var size = new Size(pixelSize.Width, pixelSize.Height);
        renderCanvas.Measure(size);
        renderCanvas.Arrange(new Rect(0, 0, size.Width, size.Height));

        var renderTarget = new RenderTargetBitmap(pixelSize);
        renderTarget.Render(renderCanvas);
        return renderTarget;
    }

    private void ClearCommands()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _activeCommand = null;
        _activeControl = null;
        _isDrawing = false;

        // 保留第一个子元素（ImageContent 底），清掉所有命令控件
        while (DrawingCanvas.Children.Count > 1)
        {
            DrawingCanvas.Children.RemoveAt(DrawingCanvas.Children.Count - 1);
        }
    }

    private void ApplyViewTransform()
    {
        var matrix = Matrix.CreateScale(_viewScale, _viewScale)
                     * Matrix.CreateTranslation(_viewOffset.X, _viewOffset.Y);
        DrawingCanvas.RenderTransform = new MatrixTransform(matrix);
    }

    private static double ClampScale(double scale) => Math.Clamp(scale, MinViewScale, MaxViewScale);

    /// <summary>
    /// 把图中心约束在视口内（图永远不完全飞出视口）。
    /// </summary>
    private Point ClampViewOffset()
    {
        if (_source == null) return _viewOffset;
        double centerX = DrawingCanvas.Width / 2 * _viewScale;
        double centerY = DrawingCanvas.Height / 2 * _viewScale;
        double cx = Math.Clamp(_viewOffset.X + centerX, 0, Math.Max(0, Bounds.Width));
        double cy = Math.Clamp(_viewOffset.Y + centerY, 0, Math.Max(0, Bounds.Height));
        return new Point(cx - centerX, cy - centerY);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!AllowViewZoom || e.Delta.Y == 0 || _source == null) return;

        var mouse = e.GetPosition(this);
        var point = e.GetPosition(DrawingCanvas);
        double newScale = ClampScale(_viewScale * Math.Pow(ScaleStep, e.Delta.Y));
        if (Math.Abs(newScale - _viewScale) < 0.0001) return;

        // 光标锚定：缩放后 point 仍应停在 mouse 处
        _viewOffset = new Point(mouse.X - point.X * newScale, mouse.Y - point.Y * newScale);
        _viewScale = newScale;
        // 兜底：图中心永远被约束在视口内，缩放/平移不会把图完全甩出视口（图“不见了”）
        _viewOffset = ClampViewOffset();
        ApplyViewTransform();
        ViewChanged?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_source == null) return;

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            _panning = true;
            _panStartView = e.GetPosition(this);
            _panStartOffset = _viewOffset;
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;
        if (_activeTextBox != null && ReferenceEquals(e.Source, _activeTextBox)) return;

        StartDraw(e);
        // 不 Handled：左键按下要继续冒泡到宿主窗口（双击关闭检测在窗口层）
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_panning)
        {
            var p = e.GetPosition(this);
            _viewOffset = _panStartOffset + (p - _panStartView);
            ApplyViewTransform();
            ViewChanged?.Invoke();
            return;
        }

        if (_isDrawing) UpdateDraw(e, e.GetPosition(DrawingCanvas));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased)
        {
            _panning = false;
            return;
        }

        if (_isDrawing)
        {
            FinishDraw();
            e.Handled = true;
        }
    }

    private void StartDraw(PointerPressedEventArgs e)
    {
        _startPoint = e.GetPosition(DrawingCanvas);
        _currentPoint = _startPoint;
        _isDrawing = true;
        _redoStack.Clear();
        UndoRedoChanged?.Invoke();

        if (_currentTool == DrawingTool.Text)
        {
            StartTextInput(_startPoint);
            return;
        }

        _activeCommand = CreateCommand(_currentTool, _startPoint);
        _activeControl = _activeCommand.CreateControl();
        DrawingCanvas.Children.Add(_activeControl);
    }

    private IDrawCommand CreateCommand(DrawingTool tool, Point start) => tool switch
    {
        DrawingTool.Rectangle => new RectangleCommand(start, start, _currentColor, _strokeThickness),
        DrawingTool.Ellipse => new EllipseCommand(start, start, _currentColor, _strokeThickness),
        DrawingTool.Line => new LineCommand(start, start, _currentColor, _strokeThickness),
        DrawingTool.ArrowLine => new ArrowLineCommand(start, start, _currentColor, _strokeThickness),
        DrawingTool.Text => new TextCommand(start, _currentColor, _fontSize, string.Empty),
        DrawingTool.Pen => new PenCommand(start, _currentColor, _strokeThickness),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
    };

    private void UpdateDraw(PointerEventArgs e, Point point)
    {
        _currentPoint = ConstrainPointToBounds(point, GetDrawBounds());
        // Shift：矩形/椭圆约束成正方形/正圆（自由画笔不受影响）
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _activeCommand is RectangleCommand or EllipseCommand)
        {
            _currentPoint = ConstrainSquare(_startPoint, _currentPoint);
        }

        if (_activeCommand == null || _activeControl == null) return;
        if (_activeCommand is PenCommand pen) pen.AddPoint(_currentPoint);
        _activeCommand.UpdateControl(_activeControl, _currentPoint);
    }

    private void FinishDraw()
    {
        _isDrawing = false;
        if (_activeCommand == null || _activeControl == null)
        {
            _activeCommand = null;
            _activeControl = null;
            return;
        }

        bool valid = _activeCommand is PenCommand pen ? pen.Points.Count >= 2 : _startPoint != _currentPoint;
        if (!valid)
        {
            // 没画出来（点了一下 / 自由画笔单点）：移除，不入栈
            DrawingCanvas.Children.Remove(_activeControl);
        }
        else
        {
            _undoStack.Push(new UndoEntry(_activeCommand, _activeControl));
        }

        _activeCommand = null;
        _activeControl = null;
        UndoRedoChanged?.Invoke();
    }

    private void StartTextInput(Point position)
    {
        // 先落掉上一个输入框：新框 Focus() 会让旧框失焦，而那时 _activeTextBox 已被换成新框，
        // 失焦回调会按错目标提交（新空框），旧框里的文字就此丢出命令栈
        CommitActiveText();

        var textBox = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(_currentColor),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray),
            FontSize = _fontSize,
            AcceptsReturn = false,
            Text = ""
        };
        Canvas.SetLeft(textBox, position.X);
        Canvas.SetTop(textBox, position.Y - _fontSize);
        textBox.LostFocus += OnTextLostFocus;
        textBox.KeyDown += OnTextKeyDown;
        DrawingCanvas.Children.Add(textBox);
        _activeTextBox = textBox;
        _isDrawing = false;
        textBox.Focus();
        textBox.SelectAll();
    }

    private void OnTextLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitActiveText();
    }

    private void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _activeTextBox == null) return;
        CommitActiveText();
        e.Handled = true;
    }

    // 输入中的文字统一在这里提交（失焦、回车、导出前三条路径共用）：
    // 退订事件、摘掉输入框、空文本不入栈
    private void CommitActiveText()
    {
        if (_activeTextBox == null) return;
        TextBox textBox = _activeTextBox;
        _activeTextBox = null;
        textBox.LostFocus -= OnTextLostFocus;
        textBox.KeyDown -= OnTextKeyDown;
        CommitText(textBox);
    }

    private void CommitText(TextBox textBox)
    {
        DrawingCanvas.Children.Remove(textBox);
        if (string.IsNullOrEmpty(textBox.Text)) return;

        // 输入框内边距补偿（TextBox 文字四周留白），落到图上 TextBlock 的位置
        var command = new TextCommand(
            new Point(Canvas.GetLeft(textBox) + 9, Canvas.GetTop(textBox) + 7),
            _currentColor, _fontSize, textBox.Text);
        Control control = command.CreateControl();
        DrawingCanvas.Children.Add(control);
        _undoStack.Push(new UndoEntry(command, control));
        UndoRedoChanged?.Invoke();
    }

    private Rect GetDrawBounds()
    {
        if (_source == null) return default;
        return new Rect(0, 0, _source.PixelSize.Width, _source.PixelSize.Height);
    }

    private static Point ConstrainSquare(Point start, Point end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double side = Math.Min(Math.Abs(dx), Math.Abs(dy));
        return new Point(start.X + Math.Sign(dx) * side, start.Y + Math.Sign(dy) * side);
    }

    private static Point ConstrainPointToBounds(Point point, Rect bounds)
    {
        return new Point(
            Math.Clamp(point.X, bounds.Left, bounds.Right),
            Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
    }

    private sealed record UndoEntry(IDrawCommand Command, Control Control);
}
