using System.Runtime.InteropServices;
using HPPH;
using ScreenCapture.NET;

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
        return await Task.Run<IImage?>(() => Capture(screenId));
    }

    /// <summary>
    /// 对指定屏幕进行全屏截图
    /// </summary>
    /// <param name="screenId"></param>
    /// <returns></returns>
    public static IImage? Capture(int screenId)
    {
        IEnumerable<Display> displays = ScreenCaptureService.GetDisplays(GraphicsCard);
        foreach (var display in displays)
        {
            if (display.Index == screenId)
            {
                ScreenCapture.NET.IScreenCapture screenCapture = ScreenCaptureService.GetScreenCapture(display);

                ICaptureZone fullscreen = screenCapture.RegisterCaptureZone(0, 0, screenCapture.Display.Width,
                    screenCapture.Display.Height);

                if (!screenCapture.CaptureScreen())
                {
                    return null;
                }

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