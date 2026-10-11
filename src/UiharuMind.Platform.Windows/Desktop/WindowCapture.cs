/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.InteropServices;
using SkiaSharp;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using static UiharuMind.Platform.Windows.Desktop.WindowsNative;

namespace UiharuMind.Platform.Windows.Desktop;

/// <summary>
/// 截一个窗口（压在上面的窗口不截）：PrintWindow 让窗口自己画进内存，
/// 再裁到看得见的外框——与报给宿主的窗口位置一一对应，认出的字才落得回原处
/// </summary>
internal static class WindowCapture
{
    /// <summary>
    /// 截图
    /// </summary>
    /// <param name="hwnd">窗口</param>
    /// <returns>PNG</returns>
    /// <exception cref="AxActionException">最小化或截不到</exception>
    public static byte[] Capture(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) throw new AxActionException("The window is minimized, so its picture can't be read. Restore it first.");
        GetWindowRect(hwnd, out Rect whole);
        Rect frame = FrameBounds(hwnd);
        if (whole.Width <= 0 || whole.Height <= 0) throw new AxActionException("The window has no size on screen.");

        IntPtr windowDc = GetWindowDC(hwnd);
        IntPtr memoryDc = CreateCompatibleDC(windowDc);
        BitmapInfoHeader header = new()
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = whole.Width,
            Height = -whole.Height, //负数：自上而下
            Planes = 1,
            BitCount = 32,
        };
        IntPtr bitmap = CreateDIBSection(windowDc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr previous = SelectObject(memoryDc, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memoryDc, PwRenderFullContent))
            {
                throw new AxActionException("Capturing the window failed: it did not draw itself (some games and video players don't).");
            }

            int left = Math.Clamp(frame.Left - whole.Left, 0, whole.Width - 1);
            int top = Math.Clamp(frame.Top - whole.Top, 0, whole.Height - 1);
            int width = Math.Clamp(frame.Width, 1, whole.Width - left);
            int height = Math.Clamp(frame.Height, 1, whole.Height - top);
            return EncodePng(bits, whole.Width, left, top, width, height);
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(hwnd, windowDc);
        }
    }

    private static byte[] EncodePng(IntPtr bits, int stride, int left, int top, int width, int height)
    {
        using SKBitmap whole = new();
        SKImageInfo info = new(stride, top + height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        whole.InstallPixels(info, bits, stride * 4);
        using SKBitmap cropped = new(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (!whole.ExtractSubset(cropped, SKRectI.Create(left, top, width, height)))
        {
            throw new AxActionException("Capturing the window failed.");
        }

        using SKImage image = SKImage.FromBitmap(cropped);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
