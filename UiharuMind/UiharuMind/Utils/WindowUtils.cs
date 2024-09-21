using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using UiharuMind.Core.Input;

namespace UiharuMind.Utils;

public static class WindowUtils
{
    public static void SetWindowToMousePosition(this Window window,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top, int offsetX = 0, int offsetY = 0)
    {
        double posX = InputManager.MouseData.X;
        double posY = InputManager.MouseData.Y;
        switch (horizontalAlignment)
        {
            case HorizontalAlignment.Left:
                posX -= window.Width;
                break;
            case HorizontalAlignment.Center:
                posX -= window.Width / 2;
                break;
            case HorizontalAlignment.Right:
                break;
        }

        switch (verticalAlignment)
        {
            case VerticalAlignment.Top:
                posY -= window.Height;
                break;
            case VerticalAlignment.Center:
                posY -= window.Height / 2;
                break;
            case VerticalAlignment.Bottom:
                break;
        }

        var scaling = App.ScreensService.MouseScreen?.Scaling ?? 1;

        window.Position = new PixelPoint((int)(posX + offsetX * scaling), (int)(posY + offsetY * scaling));
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