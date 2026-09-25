using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using UiharuMind.Features.ScreenCapture.Drawing;

namespace UiharuMind.Features.ScreenCapture;

/// <summary>
/// 贴图窗编辑模式的工具条（由停靠窗挂载）。只负责按钮与取色器外观，
/// 不持有编辑器——所有操作经事件交回宿主（贴图窗）转发给编辑器。
/// </summary>
public partial class ScreenCaptureEditToolbar : UserControl
{
    private DrawingTool _currentTool = DrawingTool.Rectangle;

    /// <summary>工具切换（宿主转发给编辑器）</summary>
    public event Action<DrawingTool>? ToolChanged;

    /// <summary>取色（宿主转发给编辑器）</summary>
    public event Action<Color>? ColorChanged;

    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? SaveRequested;
    public event Action? CancelRequested;

    public ScreenCaptureEditToolbar()
    {
        InitializeComponent();

        GeometryRectangleButton.IsCheckedChanged += (_, _) => UpdateTool(GeometryRectangleButton, DrawingTool.Rectangle);
        GeometryEllipseButton.IsCheckedChanged += (_, _) => UpdateTool(GeometryEllipseButton, DrawingTool.Ellipse);
        GeometryLineButton.IsCheckedChanged += (_, _) => UpdateTool(GeometryLineButton, DrawingTool.Line);
        GeometryArrowLineButton.IsCheckedChanged += (_, _) => UpdateTool(GeometryArrowLineButton, DrawingTool.ArrowLine);
        GeometryTextButton.IsCheckedChanged += (_, _) => UpdateTool(GeometryTextButton, DrawingTool.Text);
        GeometryPenButton.IsCheckedChanged += (_, _) => UpdateTool(GeometryPenButton, DrawingTool.Pen);

        GeometryColorPicker.ColorChanged += (_, e) => ColorChanged?.Invoke(e.NewColor);
        UndoButton.Click += (_, _) => UndoRequested?.Invoke();
        RedoButton.Click += (_, _) => RedoRequested?.Invoke();
        SaveButton.Click += (_, _) => SaveRequested?.Invoke();
        CancelButton.Click += (_, _) => CancelRequested?.Invoke();
    }

    /// <summary>设置当前工具并回显按钮（宿主进入编辑模式时调用）</summary>
    /// <param name="tool">工具</param>
    public void SetTool(DrawingTool tool)
    {
        _currentTool = tool;
        UpdateToolRender();
    }

    /// <summary>设置当前颜色并回显取色器（宿主进入编辑模式时调用）</summary>
    /// <param name="color">颜色</param>
    public void SetColor(Color color) => GeometryColorPicker.Color = color;

    /// <summary>刷新撤销/重做可用性（编辑器命令栈变化时由宿主转发）</summary>
    /// <param name="canUndo">可撤销</param>
    /// <param name="canRedo">可重做</param>
    public void SetUndoRedo(bool canUndo, bool canRedo)
    {
        UndoButton.IsEnabled = canUndo;
        RedoButton.IsEnabled = canRedo;
    }

    // 用户把当前工具再点一下 = 取消选中，没有活动工具（同独立编辑窗行为）；
    // 换工具时旧按钮的 IsCheckedChanged(false) 会追来，此时不动当前工具
    private void UpdateTool(ToggleButton sender, DrawingTool tool)
    {
        if (sender.IsChecked != true)
        {
            if (tool == _currentTool) _currentTool = DrawingTool.Max;
            return;
        }

        _currentTool = tool;
        ToolChanged?.Invoke(tool);
        UpdateToolRender();
    }

    private void UpdateToolRender()
    {
        GeometryRectangleButton.IsChecked = _currentTool == DrawingTool.Rectangle;
        GeometryEllipseButton.IsChecked = _currentTool == DrawingTool.Ellipse;
        GeometryLineButton.IsChecked = _currentTool == DrawingTool.Line;
        GeometryArrowLineButton.IsChecked = _currentTool == DrawingTool.ArrowLine;
        GeometryTextButton.IsChecked = _currentTool == DrawingTool.Text;
        GeometryPenButton.IsChecked = _currentTool == DrawingTool.Pen;
    }
}
