using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.ScreenCapture.Overlay;

/// <summary>
/// 跟随光标的信息面板：悬停时显示放大镜那一页，框选时换成位置/尺寸那一页。
/// 面板自身的定位（贴着光标、不越出屏幕）与两页的切换都收在这里，内容各自由所属组件填。
/// </summary>
internal sealed class CaptureInfoPanel
{
    private static readonly Size PointerOffset = new(25, 25); //面板与光标的间距

    private readonly Border _panel;
    private readonly Control _magnifierPage;
    private readonly Control _selectionPage;
    private readonly TextBlock _positionText;
    private readonly TextBlock _resolutionText;

    public CaptureInfoPanel(Border panel, Control magnifierPage, Control selectionPage,
        TextBlock positionText, TextBlock resolutionText)
    {
        _panel = panel;
        _magnifierPage = magnifierPage;
        _selectionPage = selectionPage;
        _positionText = positionText;
        _resolutionText = resolutionText;
    }

    public void ShowMagnifierPage()
    {
        _magnifierPage.IsVisible = true;
        _selectionPage.IsVisible = false;
    }

    public void ShowSelectionPage()
    {
        _magnifierPage.IsVisible = false;
        _selectionPage.IsVisible = true;
    }

    /// <summary>第一次拿到可信光标位置后才显示，否则刚打开会在左上角闪一下</summary>
    public void Reveal()
    {
        if (!_panel.IsVisible) _panel.IsVisible = true;
    }

    public void Hide()
    {
        _panel.IsVisible = false;
    }

    /// <summary>还没开始框选时，尺寸一栏显示整块屏幕的物理分辨率</summary>
    public void ShowScreenSize(Screen screen, double renderScaling, PixelPoint pointer, PixelPoint windowOrigin)
    {
        double boundsToPixels = DisplayUnits.ScreenBoundsToPixels(screen.Scaling, renderScaling);
        Update(screen, renderScaling, pointer, windowOrigin,
            screen.Bounds.Width * boundsToPixels, screen.Bounds.Height * boundsToPixels);
    }

    /// <summary>框选中，尺寸一栏显示选区的物理像素尺寸</summary>
    /// <param name="screen">当前屏幕</param>
    /// <param name="renderScaling">遮罩窗的 RenderScaling</param>
    /// <param name="pointer">指针位置（屏幕坐标系）</param>
    /// <param name="windowOrigin">遮罩窗左上（Position 口径）</param>
    /// <param name="selectionDip">选区尺寸（窗内 DIP）</param>
    public void ShowSelectionSize(Screen screen, double renderScaling, PixelPoint pointer, PixelPoint windowOrigin,
        Size selectionDip)
    {
        double pixelsPerDip = DisplayUnits.PixelsPerDip(screen.Scaling, renderScaling);
        Update(screen, renderScaling, pointer, windowOrigin,
            selectionDip.Width * pixelsPerDip, selectionDip.Height * pixelsPerDip);
    }

    /// <summary>只挪位置不改内容（放大镜那一页的文字由放大镜自己填）</summary>
    public void FollowPointer(Screen screen, PixelPoint pointer, PixelPoint windowOrigin)
    {
        var position = UiUtils.EnsurePositionWithinScreen(screen, pointer, _panel.Bounds.Size, PointerOffset);
        Point point = position.ToPoint(screen.Scaling);
        // Margin 是窗内相对坐标，屏幕坐标要先减掉窗口原点（主屏原点为 0 才一直没暴露）
        Point origin = windowOrigin.ToPoint(screen.Scaling);
        _panel.Margin = new Thickness(Math.Floor(point.X - origin.X), Math.Floor(point.Y - origin.Y), 0, 0);
    }

    private void Update(Screen screen, double renderScaling, PixelPoint pointer, PixelPoint windowOrigin,
        double widthPixels, double heightPixels)
    {
        try
        {
            double boundsToPixels = DisplayUnits.ScreenBoundsToPixels(screen.Scaling, renderScaling);
            int x = Math.Clamp((int)Math.Round(pointer.X * boundsToPixels), 0,
                (int)(screen.Bounds.Width * boundsToPixels));
            int y = Math.Clamp((int)Math.Round(pointer.Y * boundsToPixels), 0,
                (int)(screen.Bounds.Height * boundsToPixels));
            _positionText.Text = $"{Lang.ScreenCapturePosition}:({x},{y})";
            _resolutionText.Text =
                $"{Lang.ScreenCaptureResolution}:({(int)Math.Ceiling(widthPixels)}x{(int)Math.Ceiling(heightPixels)})";
            FollowPointer(screen, pointer, windowOrigin);
        }
        catch (Exception e)
        {
            Log.Warning(e.StackTrace);
        }
    }
}
