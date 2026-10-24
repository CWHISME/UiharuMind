/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Runtime.InteropServices;
using HPPH;
using ScreenCapture.NET;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.UiharuScreenCapture;

public static class ScreenCaptureWin
{
    private static readonly IScreenCaptureService ScreenCaptureService = new DX11ScreenCaptureService();
    private static readonly GraphicsCard GraphicsCard = ScreenCaptureService.GetGraphicsCards().First();

    private static bool _hasCaptured = false;

    /// <summary>
    /// DX11 捕获服务按屏幕缓存了它的 <see cref="IScreenCapture"/>（见 GetScreenCapture），
    /// 并发请求会共用同一实例，注册/捕获/注销不能穿插，把整段串行化。
    /// </summary>
    private static readonly object CaptureGate = new();

    public static async Task<IImage?> CaptureAsync(int screenId)
    {
        // Log.Debug("开始截图");
        return await Task.Run<IImage?>(() => Capture(screenId));
    }

    /// <summary>
    /// 对指定屏幕进行全屏截图
    /// </summary>
    /// <param name="screenId"></param>
    /// <returns></returns>
    public static IImage? Capture(int screenId)
    {
        lock (CaptureGate)
        {
            foreach (var display in ScreenCaptureService.GetDisplays(GraphicsCard))
            {
                if (display.Index != screenId) continue;

                // 项目自身也有 IScreenCapture 接口，这里必须是库的全限定名，否则会被遮蔽。
                ScreenCapture.NET.IScreenCapture screenCapture = ScreenCaptureService.GetScreenCapture(display);

                // 每个 zone 都持有 width×height×4 的像素缓冲（托管）和一份 DXGI staging 纹理（非托管）。
                // 只注册不注销会把历次截图的缓冲全挂在 screenCapture 的字典里（4K 屏每份约 33 MB），
                // 必须捕获完注销掉；返回的 image 包装了 zone 的 InternalBuffer，注销后仍可安全读取，
                // 它随调用方的帧对象一起 GC，不会悬空。
                ICaptureZone fullscreen = screenCapture.RegisterCaptureZone(0, 0, screenCapture.Display.Width,
                    screenCapture.Display.Height);
                try
                {
                    if (!_hasCaptured)
                    {
                        _hasCaptured = true;
                        Thread.Sleep(100); // 首次注册后等 DXGI duplication 就绪
                    }

                    Thread.Sleep(1);
                    if (!screenCapture.CaptureScreen())
                    {
                        return null;
                    }

                    using (fullscreen.Lock())
                    {
                        return fullscreen.Image;
                    }
                }
                finally
                {
                    screenCapture.UnregisterCaptureZone(fullscreen);
                }
            }

            return null;
        }
    }

    public static async Task GetCaptureImagePointerAsync(this IImage image, Action<IntPtr, int> onCaptureSuccess)
    {
        await Task.Run(() => GetCaptureImagePointer(image, onCaptureSuccess));
    }

    /// <summary>
    /// IntPtr 在函数调用完毕后会自动释放
    /// </summary>
    /// <param name="image"></param>
    /// <param name="onCaptureSuccess">data and stride</param>
    public static void GetCaptureImagePointer(this IImage image, Action<IntPtr, int> onCaptureSuccess)
    {
        var width = image.Width;
        var height = image.Height;
        var stride = width * 4; // 每个像素 4 字节 (ARGB)
        var dataSize = stride * height;
        var data = Marshal.AllocHGlobal(dataSize);
        try
        {
            unsafe
            {
                var ptr = (byte*)data.ToPointer();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var color = image[x, y];
                        ptr[0] = color.B;
                        ptr[1] = color.G;
                        ptr[2] = color.R;
                        ptr[3] = color.A;
                        ptr += 4;
                    }
                }
            }

            onCaptureSuccess.Invoke(data, stride);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}