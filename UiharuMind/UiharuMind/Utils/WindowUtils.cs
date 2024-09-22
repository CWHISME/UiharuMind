using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Core.Input;

namespace UiharuMind.Utils;

public static class WindowUtils
{
    public static void SetWindowToMousePosition(this Window window,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top, int offsetX = 0, int offsetY = 0)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var pos = App.ScreensService.MousePosition;
            var scaling = App.ScreensService.Scaling;
            double posX = pos.X;
            double posY = pos.Y;
            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    posX -= window.Width * scaling;
                    break;
                case HorizontalAlignment.Center:
                    posX -= window.Width / 2 * scaling;
                    break;
                case HorizontalAlignment.Right:
                    break;
            }

            switch (verticalAlignment)
            {
                case VerticalAlignment.Top:
                    posY -= window.Height * scaling;
                    break;
                case VerticalAlignment.Center:
                    posY -= window.Height / 2 * scaling;
                    break;
                case VerticalAlignment.Bottom:
                    break;
            }

            window.Position = new PixelPoint((int)(posX + offsetX * scaling), (int)(posY + offsetY * scaling));
        });
    }

    public static void SetSimpledecorationWindow(this Window window, bool isTopmost = true)
    {
        window.Topmost = isTopmost;
        window.WindowState = WindowState.Normal;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.CanResize = false;
        window.SystemDecorations = SystemDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = -1;
    }
}