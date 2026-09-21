using System.Runtime.InteropServices;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input.Mac;

/// <summary>
/// macOS 的全局光标定位器，走 CoreGraphics 直接查当前光标位置。
/// <para>
/// 钩子事件确实自带坐标，但那是「事件来过之后」才有——应用刚启动、用户还没动过鼠标时，
/// 缓存的坐标还是 (0,0)，跟随鼠标弹出的窗口会跑到屏幕角上。带外查询没有这个初始空窗期。
/// </para>
/// </summary>
public sealed class MacPointerLocator : IPointerLocator
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public bool IsAvailable => true;

    public bool TryGetPosition(out short x, out short y)
    {
        x = 0;
        y = 0;

        var cgEvent = IntPtr.Zero;
        try
        {
            cgEvent = CGEventCreate(IntPtr.Zero);
            if (cgEvent == IntPtr.Zero) return false;

            var location = CGEventGetLocation(cgEvent);
            x = (short)location.X;
            y = (short)location.Y;
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to query macOS pointer position: {e.Message}");
            return false;
        }
        finally
        {
            if (cgEvent != IntPtr.Zero) CFRelease(cgEvent);
        }
    }

    public void Dispose()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr cgEvent);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
