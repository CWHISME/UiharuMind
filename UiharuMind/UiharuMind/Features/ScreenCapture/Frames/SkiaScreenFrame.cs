using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.ScreenCapture.Frames;

/// <summary>
/// Linux 的整屏帧：底层是 Portal 交回的 PNG 解码结果。
/// Portal 给的是覆盖整个桌面的一张图，构造时先裁到目标屏，使本帧与 Windows 侧语义一致。
/// 保留 SKBitmap 而非只留 Avalonia 位图，是因为裁剪需要按矩形取子图，Avalonia 的 Bitmap 没有这个能力。
/// </summary>
public sealed class SkiaScreenFrame : IScreenFrame
{
    private SKBitmap? _source; //本屏像素,与 Display 是同一张图的两份表示,一起释放
    private Bitmap? _display;

    public Bitmap Display => _display!;

    public PixelPoint Origin { get; }

    public PixelSize PixelSize => _source == null ? default : new PixelSize(_source.Width, _source.Height);

    private SkiaScreenFrame(SKBitmap source, Bitmap display, PixelPoint origin)
    {
        _source = source;
        _display = display;
        Origin = origin;
    }

    /// <summary>
    /// 从整桌面 PNG 流构造某一块屏幕的整屏帧
    /// </summary>
    /// <param name="pngStream">覆盖整个桌面的 PNG 数据流</param>
    /// <param name="screenBounds">目标屏幕在桌面坐标系中的像素矩形</param>
    /// <returns>构造成功返回帧对象，解码失败返回 null</returns>
    public static SkiaScreenFrame? TryCreate(Stream pngStream, PixelRect screenBounds)
    {
        try
        {
            using var buffer = new MemoryStream();
            pngStream.CopyTo(buffer);
            buffer.Position = 0;

            using var desktop = SKBitmap.Decode(buffer);
            if (desktop == null)
            {
                Log.Warning("整屏 PNG 解码失败。");
                return null;
            }

            var region = screenBounds.Intersect(new PixelRect(0, 0, desktop.Width, desktop.Height));
            if (region.Width <= 0 || region.Height <= 0)
            {
                Log.Warning("目标屏幕不在 Portal 返回的桌面图范围内。");
                return null;
            }

            var source = new SKBitmap();
            if (!desktop.ExtractSubset(source, SKRectI.Create(region.X, region.Y, region.Width, region.Height)))
            {
                source.Dispose();
                Log.Warning("从桌面图裁出目标屏失败。");
                return null;
            }

            var display = Encode(source);
            if (display != null) return new SkiaScreenFrame(source, display, region.Position);

            source.Dispose();
            return null;
        }
        catch (Exception e)
        {
            Log.Warning($"构造整屏帧失败：{e.Message}");
            return null;
        }
    }

    public Bitmap? Crop(PixelRect desktopRegion)
    {
        if (_source == null) return null;

        var local = desktopRegion.Translate(-(PixelVector)Origin)
            .Intersect(new PixelRect(0, 0, _source.Width, _source.Height));
        if (local.Width <= 0 || local.Height <= 0) return null;

        try
        {
            using var subset = new SKBitmap();
            if (!_source.ExtractSubset(subset, SKRectI.Create(local.X, local.Y, local.Width, local.Height)))
            {
                Log.Warning("裁剪整屏失败：ExtractSubset 返回 false。");
                return null;
            }

            return Encode(subset);
        }
        catch (Exception e)
        {
            Log.Warning($"裁剪整屏失败：{e.Message}");
            return null;
        }
    }

    //Avalonia 的 Bitmap 只能从流构造,这里经一次 PNG 编解码交接;截图场景一次操作只走一两次,代价可接受
    private static Bitmap? Encode(SKBitmap bitmap)
    {
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = encoded.AsStream();
            return new Bitmap(stream);
        }
        catch (Exception e)
        {
            Log.Warning($"位图编码失败：{e.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _display?.Dispose();
        _display = null;
        _source?.Dispose();
        _source = null;
    }
}
