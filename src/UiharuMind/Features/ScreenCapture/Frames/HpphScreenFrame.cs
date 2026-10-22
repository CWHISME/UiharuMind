using System;
using Avalonia;
using Avalonia.Media.Imaging;
using HPPH;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Shared.Utils;
using MediaColor = Avalonia.Media.Color;

namespace UiharuMind.Features.ScreenCapture.Frames;

/// <summary>
/// Windows 的整屏帧：底层是 HPPH 的托管像素数组，裁剪直接走它的矩形索引器，不经编解码。
/// </summary>
public sealed class HpphScreenFrame : IScreenFrame
{
    private readonly IImage _image; //HPPH 托管像素数组,非 IDisposable,随本对象一起交给 GC
    private Bitmap? _display;

    public Bitmap Display => _display!;

    public PixelPoint Origin { get; }

    public PixelSize PixelSize => new(_image.Width, _image.Height);

    private HpphScreenFrame(IImage image, Bitmap display, PixelPoint origin)
    {
        _image = image;
        _display = display;
        Origin = origin;
    }

    /// <summary>
    /// 用一帧 HPPH 图像构造整屏帧
    /// </summary>
    /// <param name="image">目标屏幕的整屏像素</param>
    /// <param name="origin">该屏左上角在桌面坐标系中的位置</param>
    /// <returns>构造成功返回帧对象，位图转换失败返回 null</returns>
    public static HpphScreenFrame? TryCreate(IImage image, PixelPoint origin)
    {
        var display = image.ImageToBitmap();
        if (display != null) return new HpphScreenFrame(image, display, origin);

        Log.Warning("整屏像素转位图失败。");
        return null;
    }

    public Bitmap? Crop(PixelRect desktopRegion)
    {
        var local = desktopRegion.Translate(-(PixelVector)Origin)
            .Intersect(new PixelRect(0, 0, _image.Width, _image.Height));
        if (local.Width <= 0 || local.Height <= 0) return null;

        try
        {
            return _image[local.X, local.Y, local.Width, local.Height].ImageToBitmap();
        }
        catch (Exception e)
        {
            Log.Warning($"裁剪整屏失败：{e.Message}");
            return null;
        }
    }

    public MediaColor? SampleColor(PixelPoint screenUnits)
    {
        int x = screenUnits.X - Origin.X;
        int y = screenUnits.Y - Origin.Y;
        if (x < 0 || y < 0 || x >= _image.Width || y >= _image.Height) return null;

        try
        {
            // 直接走 HPPH 索引器读单个像素，O(1) 零分配。
            // 之前经 GetCaptureImagePointer 取一个点会先复制整屏（4K 约 830 万次遍历），
            // 鼠标每动一下都来一回，UI 线程被反复堵死，tips 跟随就卡了
            var c = _image[x, y];
            return MediaColor.FromArgb(c.A, c.R, c.G, c.B);
        }
        catch (Exception e)
        {
            Log.Warning($"取色失败：{e.Message}");
        }

        return null;
    }

    /// 整屏位图是全应用最大的一次分配，且每次截图都来一张，必须确定性释放
    public void Dispose()
    {
        _display?.Dispose();
        _display = null;
    }
}
