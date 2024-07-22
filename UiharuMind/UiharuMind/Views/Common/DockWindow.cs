using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Views.Common;

public class DockWindow<T> : Window where T : Window
{
    protected T? CurrentSnapWindow;

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

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        MainWindow_OnMouseLeave(this, e);
    }

    public void SetMainWindow(T? mainWindow)
    {
        if (mainWindow == null)
        {
            Hide();
            return;
        }

        if (ReferenceEquals(mainWindow, CurrentSnapWindow))
        {
            Show();
            UpdateFollowerWindowPosition();
            return;
        }

        if (CurrentSnapWindow != null)
        {
            CurrentSnapWindow.PositionChanged -= MainWindow_PositionChanged;
            CurrentSnapWindow.SizeChanged -= MainWindow_SizeChanged;
            CurrentSnapWindow.PointerExited -= MainWindow_OnMouseLeave;
            CurrentSnapWindow.Closing -= MainWindow_OnClose;
        }

        CurrentSnapWindow = mainWindow;

        CurrentSnapWindow.PositionChanged += MainWindow_PositionChanged;
        CurrentSnapWindow.SizeChanged += MainWindow_SizeChanged;
        CurrentSnapWindow.PointerExited += MainWindow_OnMouseLeave;
        CurrentSnapWindow.Closing += MainWindow_OnClose;

        Show();
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_OnClose(object? sender, WindowClosingEventArgs e)
    {
        SetMainWindow(null);
    }

    private void MainWindow_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
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
        if (CurrentSnapWindow == null) return false;

        var mousePos = App.ScreensService.MousePosition;
        //检测是否处于组合区域内
        var selfWindowBounds = new Rect(this.Position.X, this.Position.Y, this.Width, this.Height);
        var targetWindowBounds = new Rect(CurrentSnapWindow.Position.X, CurrentSnapWindow.Position.Y,
            CurrentSnapWindow.Width, CurrentSnapWindow.Height);
        // 计算组合区域，包括两个窗口之间的间距
        var combinedBounds = selfWindowBounds.Union(targetWindowBounds);
        if (!combinedBounds.Contains(new Point(mousePos.X, mousePos.Y))) return false;

        // Log.Debug(
        //     $" mousePos:{mousePos} targetWindowBounds:{targetWindowBounds.Right}  selfWindowBounds：{selfWindowBounds.Right}");

        // 根据高度决定检测 mainWindowBounds 还是 bottomWindowBounds 的宽度
        //注：靠下才这样额外检测
        if (mousePos.Y < targetWindowBounds.Bottom && mousePos.X < targetWindowBounds.Right)
        {
            // 位于目标(上方)窗口高度内，且处于其宽度内
            return true;
        }

        //位于底部窗口高度内，且处于其宽度内
        if (mousePos.Y < selfWindowBounds.Bottom && mousePos.X < selfWindowBounds.Right)
        {
            return true;
        }

        return false;
    }

    private void UpdateFollowerWindowPosition()
    {
        if (!IsVisible) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentSnapWindow == null)
                return;

            // var mainWindowPosition = _currentMainWindow.Position;
            // var mainWindowSize = _currentMainWindow.ClientSize;

            // Update the position of the dock window itself
            OnFollowTarget(CurrentSnapWindow.Position, CurrentSnapWindow.ClientSize);
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