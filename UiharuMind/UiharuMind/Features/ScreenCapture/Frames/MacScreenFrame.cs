using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.ScreenCapture.Frames;

/// <summary>
/// macOS 的整屏帧：screencapture 按屏序号静默抓到的 PNG。
/// macOS 的 Screen.Scaling 恒为 1、Bounds 是 point，PNG 却是 backing 像素（Retina 下 2 倍），
/// 所以帧内自带 PixelScale：Display 位图按 backing 标 DPI（Size 回到 point），Crop 进来的
/// point 矩形内部乘回像素。调用方（遮罩窗的选区数学）全程只见 point。
/// </summary>
public sealed class MacScreenFrame : IScreenFrame
{
    private SKBitmap? _source; //像素，裁剪用；Display 是它的独立拷贝，一起释放
    private Bitmap? _display;
    private readonly double _pixelScale;

    public Bitmap Display => _display!;

    public PixelPoint Origin { get; }

    public PixelSize PixelSize => _source == null ? default : new PixelSize(_source.Width, _source.Height);

    private MacScreenFrame(SKBitmap source, Bitmap display, PixelPoint origin, double pixelScale)
    {
        _source = source;
        _display = display;
        Origin = origin;
        _pixelScale = pixelScale;
    }

    /// <summary>
    /// 从单屏 PNG 构造整屏帧
    /// </summary>
    /// <param name="pngPath">screencapture 输出的单屏 PNG 文件</param>
    /// <param name="screenBounds">目标屏幕矩形（point，与 Screen.Bounds 同口径）</param>
    /// <returns>构造成功返回帧对象；解码失败或 PNG 与屏幕对不上（抓错屏）返回 null</returns>
    public static MacScreenFrame? TryCreate(string pngPath, PixelRect screenBounds)
    {
        SKBitmap? decoded = null;
        try
        {
            decoded = SKBitmap.Decode(pngPath);
            if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                Log.Warning("整屏 PNG 解码失败。");
                return null;
            }

            if (screenBounds.Width <= 0 || screenBounds.Height <= 0) return null;

            // 自校验：PNG 必须是屏 point 尺寸的整数倍（backing 1x/2x），否则抓错屏了
            double scaleX = (double)decoded.Width / screenBounds.Width;
            double scaleY = (double)decoded.Height / screenBounds.Height;
            if (!IsIntegerScale(scaleX) || !IsIntegerScale(scaleY) || Math.Abs(scaleX - scaleY) > 0.02)
            {
                Log.Warning($"抓到的屏幕尺寸与目标屏对不上（{decoded.Width}x{decoded.Height} vs {screenBounds.Width}x{screenBounds.Height}）。");
                return null;
            }

            double scale = Math.Round((scaleX + scaleY) / 2);
            var source = Normalize(decoded);
            decoded = null;
            if (source == null) return null;

            var display = ToBitmap(source, scale);
            if (display == null)
            {
                source.Dispose();
                return null;
            }

            return new MacScreenFrame(source, display, screenBounds.Position, scale);
        }
        catch (Exception e)
        {
            Log.Warning($"构造整屏帧失败：{e.Message}");
            return null;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    public Bitmap? Crop(PixelRect desktopRegion)
    {
        if (_source == null) return null;

        // 调用方给的是 point（含 Origin 偏移），内部乘回像素
        var scaled = new PixelRect(
            (int)Math.Round((desktopRegion.X - Origin.X) * _pixelScale),
            (int)Math.Round((desktopRegion.Y - Origin.Y) * _pixelScale),
            (int)Math.Round(desktopRegion.Width * _pixelScale),
            (int)Math.Round(desktopRegion.Height * _pixelScale));
        var local = scaled.Intersect(new PixelRect(0, 0, _source.Width, _source.Height));
        if (local.Width <= 0 || local.Height <= 0) return null;

        try
        {
            using var subset = new SKBitmap();
            if (!_source.ExtractSubset(subset, SKRectI.Create(local.X, local.Y, local.Width, local.Height)))
            {
                Log.Warning("裁剪整屏失败：ExtractSubset 返回 false。");
                return null;
            }

            // 指针构造会拷贝像素，subset 可释放；DPI 带上，预览窗直接按 point 显示
            return ToBitmap(subset, _pixelScale);
        }
        catch (Exception e)
        {
            Log.Warning($"裁剪整屏失败：{e.Message}");
            return null;
        }
    }

    private static bool IsIntegerScale(double scale)
    {
        return scale >= 0.99 && scale <= 2.01 && Math.Abs(scale - Math.Round(scale)) < 0.02;
    }

    // 统一成 Bgra8888 Unpremul；已是该格式则直接转移所有权，避免一次拷贝
    private static SKBitmap? Normalize(SKBitmap decoded)
    {
        if (decoded.ColorType == SKColorType.Bgra8888 && decoded.AlphaType == SKAlphaType.Unpremul)
            return decoded;

        try
        {
            var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var normalized = new SKBitmap(info);
            using var canvas = new SKCanvas(normalized);
            canvas.DrawBitmap(decoded, 0, 0);
            decoded.Dispose();
            return normalized;
        }
        catch (Exception e)
        {
            Log.Warning($"像素格式归一化失败：{e.Message}");
            return null;
        }
    }

    private static Bitmap? ToBitmap(SKBitmap bitmap, double scale)
    {
        try
        {
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul,
                bitmap.GetPixels(),
                new PixelSize(bitmap.Width, bitmap.Height),
                new Vector(96 * scale, 96 * scale),
                bitmap.RowBytes);
        }
        catch (Exception e)
        {
            Log.Warning($"位图构造失败：{e.Message}");
            return null;
        }
    }

    public Color? SampleColor(PixelPoint screenUnits)
    {
        if (_source == null) return null;
        int x = (int)Math.Round((screenUnits.X - Origin.X) * _pixelScale);
        int y = (int)Math.Round((screenUnits.Y - Origin.Y) * _pixelScale);
        if (x < 0 || y < 0 || x >= _source.Width || y >= _source.Height) return null;

        try
        {
            SKColor color = _source.GetPixel(x, y);
            return Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
        }
        catch (Exception e)
        {
            Log.Warning($"取色失败：{e.Message}");
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
