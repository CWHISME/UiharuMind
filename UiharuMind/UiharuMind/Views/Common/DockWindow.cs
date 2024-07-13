using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Views.Common;

public class DockWindow : Window
{
    private Window? _currentMainWindow;

    public DockWindow()
    {
        CanResize = false;
        Topmost = true;
        Background = Brushes.Transparent;
        SystemDecorations = SystemDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true; // 扩展客户端区域到装饰（标题栏）
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome; // 禁用标题栏
        ExtendClientAreaTitleBarHeightHint = 0; // 隐藏标题栏
    }

    // protected override void OnPointerMoved(PointerEventArgs e)
    // {
    //     base.OnPointerMoved(e);
    //     Log.Debug($"{e.GetPosition(this)}");
    // }

    public void SetMainWindow(Window? mainWindow)
    {
        if (_currentMainWindow != null)
        {
            _currentMainWindow.PositionChanged -= MainWindow_PositionChanged;
            _currentMainWindow.SizeChanged -= MainWindow_SizeChanged;
            _currentMainWindow.PointerExited -= MainWindow_OnMouseLeave;
        }

        _currentMainWindow = mainWindow;
        if (_currentMainWindow == null)
        {
            Hide();
            return;
        }

        _currentMainWindow.PositionChanged += MainWindow_PositionChanged;
        _currentMainWindow.SizeChanged += MainWindow_SizeChanged;
        _currentMainWindow.PointerExited += MainWindow_OnMouseLeave;

        Dispatcher.UIThread.InvokeAsync(Show);
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_PositionChanged(object sender, PixelPointEventArgs e)
    {
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_OnMouseLeave(object? sender, PointerEventArgs e)
    {
        if (!CheckInValidBounds()) SetMainWindow(null);
    }

    /// <summary>
    /// 是否处于合适区域
    /// </summary>
    /// <returns></returns>
    private bool CheckInValidBounds()
    {
        var mousePos = App.ScreensService.MousePosition;
        //检测是否处于组合区域内
        var mainWindowBounds = new Rect(this.Position.X, this.Position.Y, this.Width, this.Height);
        var bottomWindowBounds = new Rect(_currentMainWindow.Position.X, _currentMainWindow.Position.Y,
            _currentMainWindow.Width, _currentMainWindow.Height);
        // 计算组合区域，包括两个窗口之间的间距
        var combinedBounds = mainWindowBounds.Union(bottomWindowBounds);
        return combinedBounds.Contains(new Point(mousePos.X, mousePos.Y));
    }

    private void UpdateFollowerWindowPosition()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_currentMainWindow == null)
                return;

            // var mainWindowPosition = _currentMainWindow.Position;
            // var mainWindowSize = _currentMainWindow.ClientSize;

            // Update the position of the dock window itself
            OnFollowTarget(_currentMainWindow.Position, _currentMainWindow.ClientSize);
            // Width = mainWindowSize.Width + 100;
            // Height = mainWindowSize.Height + 100;

            // Update the bottom panel position and size
            // BottomPanel.Width = mainWindowSize.Width;
            // BottomPanel.Margin = new Thickness(0, 0, 0, 0);

            // Update the right panel position and size
            // RightPanel.Height = mainWindowSize.Height;
            // RightPanel.Margin = new Thickness(mainWindowSize.Width, 0, 0, 0);
        });
    }

    protected virtual void OnFollowTarget(PixelPoint targetPosition, Size targetSize)
    {
        Position = new PixelPoint(targetPosition.X, targetPosition.Y + (int)targetSize.Height + 5);
    }
}