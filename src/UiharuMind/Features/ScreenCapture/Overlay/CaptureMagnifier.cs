using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture.Frames;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.ScreenCapture.Overlay;

/// <summary>
/// 悬停放大镜与取色：以光标为中心裁一小块冻结帧放大显示，同时报物理像素坐标与该点颜色。
/// 放大走 <see cref="CroppedBitmap"/>，只是换个视图，不拷贝像素。
/// </summary>
internal sealed class CaptureMagnifier
{
    private const int Cells = 15; //放大镜横竖各多少格
    private const double CellSize = 8.0; //每格边长（DIP），15 × 8 = 120 即面板边长

    private readonly Image _image;
    private readonly Path _gridLines;
    private readonly Path _cross;
    private readonly Shape _swatch;
    private readonly TextBlock _positionText;
    private readonly TextBlock _colorText;
    private readonly TextBlock _copyHint;
    private readonly SolidColorBrush _swatchBrush = new(Colors.White);
    private readonly string _copyHintText;

    // 放大视图只建一次，之后改 SourceRect 就够了。每次移动都 new 一个的话，
    // 一次截图能攒出几千个引用着整屏底图的对象，纯浪费
    private readonly CroppedBitmap _view = new();

    private bool _showHex;
    private Color? _lastSampleColor;

    public CaptureMagnifier(Image image, Path gridLines, Path cross, Shape swatch,
        TextBlock positionText, TextBlock colorText, TextBlock copyHint, TextBlock toggleHint)
    {
        _image = image;
        _gridLines = gridLines;
        _cross = cross;
        _swatch = swatch;
        _positionText = positionText;
        _colorText = colorText;
        _copyHint = copyHint;

        _image.Source = _view;
        _swatch.Fill = _swatchBrush;
        _copyHintText = Loc.Text(LangKey.ScreenCaptureMagnifierCopyHint,
            UiharuCoreManager.Instance.IsMacOs ? "⌘" : "Ctrl");
        _copyHint.Text = _copyHintText;
        toggleHint.Text = Loc.Text(LangKey.ScreenCaptureMagnifierToggleHint);
        BuildOverlay();
    }

    /// <summary>
    /// 刷新放大镜内容
    /// </summary>
    /// <param name="frame">当前冻结帧</param>
    /// <param name="screen">帧所属屏幕</param>
    /// <param name="pointer">指针位置（屏幕坐标系，与 Screen.Bounds 同口径）</param>
    /// <param name="renderScaling">遮罩窗的 RenderScaling</param>
    public void Update(IScreenFrame frame, Screen screen, PixelPoint pointer, double renderScaling)
    {
        double toPixels = DisplayUnits.ScreenBoundsToPixels(screen.Scaling, renderScaling);
        var display = frame.Display;

        int sizePx = Math.Max(1, (int)Math.Round(Cells * toPixels));
        int centerX = (int)Math.Round((pointer.X - frame.Origin.X) * toPixels);
        int centerY = (int)Math.Round((pointer.Y - frame.Origin.Y) * toPixels);
        var bounds = new PixelRect(0, 0, display.PixelSize.Width, display.PixelSize.Height);
        var rect = new PixelRect(
            Math.Clamp(centerX - sizePx / 2, 0, Math.Max(0, bounds.Width - sizePx)),
            Math.Clamp(centerY - sizePx / 2, 0, Math.Max(0, bounds.Height - sizePx)),
            Math.Min(sizePx, bounds.Width),
            Math.Min(sizePx, bounds.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (!ReferenceEquals(_view.Source, display)) _view.Source = display;
        _view.SourceRect = rect;

        _positionText.Text =
            $"{Loc.Text(LangKey.ScreenCapturePosition)}:({(int)Math.Round(pointer.X * toPixels)}, {(int)Math.Round(pointer.Y * toPixels)})";

        var color = frame.SampleColor(pointer);
        if (color == null) return;
        _lastSampleColor = color.Value;
        RefreshColorText();
    }

    /// <summary>RGB 与 HEX 之间切换显示</summary>
    public void ToggleColorFormat()
    {
        _showHex = !_showHex;
        RefreshColorText();
    }

    /// <summary>把当前取到的颜色值写进剪贴板</summary>
    /// <param name="clipboard">目标剪贴板，可为 null</param>
    public void CopyColor(IClipboard? clipboard)
    {
        if (_lastSampleColor == null || clipboard == null) return;
        string text = _colorText.Text ?? "";
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            _ = clipboard.SetValueAsync(DataFormat.Text, text);
            _copyHint.Text = Loc.Text(LangKey.ScreenCaptureMagnifierCopied);
            DispatcherTimer.RunOnce(() => _copyHint.Text = _copyHintText, TimeSpan.FromMilliseconds(800));
        }
        catch (Exception e)
        {
            Log.Warning($"复制颜色值失败：{e.Message}");
        }
    }

    /// <summary>
    /// 断开对冻结帧的引用。<b>必须在帧释放之前调用</b>，否则渲染线程会撞上已释放的位图。
    /// 注意只置空、不 Dispose：<see cref="CroppedBitmap.Dispose"/> 会连底图一起释放，而底图归帧所有
    /// </summary>
    public void Detach()
    {
        _view.Source = null;
    }

    // 网格与十字线是静态的，建一次即可
    private void BuildOverlay()
    {
        var grid = new GeometryGroup();
        for (int i = 0; i <= Cells; i++)
        {
            double p = i * CellSize;
            grid.Children.Add(new LineGeometry(new Point(p, 0), new Point(p, Cells * CellSize)));
            grid.Children.Add(new LineGeometry(new Point(0, p), new Point(Cells * CellSize, p)));
        }

        _gridLines.Data = grid;

        var cross = new GeometryGroup();
        double mid = Cells * CellSize / 2;
        cross.Children.Add(new LineGeometry(new Point(mid, 0), new Point(mid, Cells * CellSize)));
        cross.Children.Add(new LineGeometry(new Point(0, mid), new Point(Cells * CellSize, mid)));
        _cross.Data = cross;
    }

    private void RefreshColorText()
    {
        if (_lastSampleColor == null) return;
        var color = _lastSampleColor.Value;
        _colorText.Text = _showHex
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"{color.R}, {color.G}, {color.B}";
        _swatchBrush.Color = color;
    }
}
