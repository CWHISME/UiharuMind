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

    // public ScreenCaptureWin()
    // {
    //     ScreenCaptureService = ;
    //     GraphicsCard = ScreenCaptureService.GetGraphicsCards().First();
    // }

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
        // Log.Debug("查找屏幕");
        IEnumerable<Display> displays = ScreenCaptureService.GetDisplays(GraphicsCard);
        foreach (var display in displays)
        {
            if (display.Index == screenId)
            {
                ScreenCapture.NET.IScreenCapture screenCapture = ScreenCaptureService.GetScreenCapture(display);

                // Log.Debug($"注册全屏截图 {screenCapture.Display.Width}x{screenCapture.Display.Height}");
                ICaptureZone fullscreen = screenCapture.RegisterCaptureZone(0, 0, screenCapture.Display.Width,
                    screenCapture.Display.Height);
                Thread.Sleep(1);
                // Log.Debug("真的开始截图");
                if (!screenCapture.CaptureScreen())
                {
                    return null;
                }

                // Log.Debug("锁定屏幕并获取截图");
                using (fullscreen.Lock())
                {
                    IImage image = fullscreen.Image;
                    return image;
                }
            }
        }

        return null;
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