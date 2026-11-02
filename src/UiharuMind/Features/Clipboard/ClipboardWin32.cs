using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 剪贴板要用的 Win32 / OLE 声明。监听（消息循环）与读取（OLE）两边都要碰这些，
/// 声明放一处，免得各写一份 <c>MSG</c> 与 <c>GlobalLock</c>
/// </summary>
internal static class ClipboardWin32
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const uint CF_UNICODETEXT = 13;
    public const uint PM_NOREMOVE = 0;

    /// <summary>剪贴板正被别人开着。读写都会撞上，等一等通常就过去了</summary>
    public const int ClipboardCantOpen = unchecked((int)0x800401D0);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax,
        uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>
    /// 现在开着剪贴板的那个窗口；没人开着就是 0。剪贴板是全局独占资源，
    /// <c>CLIPBRD_E_CANT_OPEN</c> 全是它惹的，查一次就知道该找谁的麻烦
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// 描述当前占着剪贴板的进程，接在失败日志后面用。<b>只在失败时调</b>：占用的窗口往往几十毫秒就放开了，
    /// 平时探测没有意义
    /// </summary>
    /// <returns>形如「，剪贴板被 Sourcetree 占着」；没人占或查不出来返回空串</returns>
    public static string DescribeHolder()
    {
        try
        {
            IntPtr holder = GetOpenClipboardWindow();
            if (holder == IntPtr.Zero) return string.Empty;
            GetWindowThreadProcessId(holder, out uint processId);
            if (processId == 0) return "，剪贴板被某个进程占着";
            using Process process = Process.GetProcessById((int)processId);
            return $"，剪贴板被 {process.ProcessName} 占着";
        }
        catch (Exception)
        {
            return "，剪贴板被某个进程占着";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    public static extern void OleUninitialize();

    /// <summary>
    /// 取剪贴板上的 OLE 数据对象。<b>返回的是 IntPtr 而不是接口</b>：P/Invoke 参数上带 COM 封送
    /// 会触发 IL2050（trim 后无法保证接口不被裁掉），改由调用方自己转成接口
    /// </summary>
    [DllImport("ole32.dll")]
    public static extern int OleGetClipboard(out IntPtr dataObject);

    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int x;
        public int y;
    }
}
