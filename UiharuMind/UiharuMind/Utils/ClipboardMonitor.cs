using System;
using System.Runtime.InteropServices;
using System.Timers;

namespace UiharuMind.Utils;

/// <summary>
/// 听说 mac 没有直接监听剪贴板的方法，只能通过定时器来检测剪贴板变化
/// </summary>
public class ClipboardMonitor
{
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);

    private Timer _clipboardCheckTimer;
    private IntPtr _pasteboard;
    private int _lastChangeCount;

    public event Action? ClipboardChanged;

    public ClipboardMonitor(double interval = 1000)
    {
        // 获取 NSPasteboard 类和 generalPasteboard 方法
        IntPtr nsPasteboardClass = objc_getClass("NSPasteboard");
        IntPtr generalPasteboardSelector = sel_registerName("generalPasteboard");
        _pasteboard = objc_msgSend_IntPtr(nsPasteboardClass, generalPasteboardSelector);

        // 获取 changeCount 方法
        IntPtr changeCountSelector = sel_registerName("changeCount");
        _lastChangeCount = objc_msgSend_int(_pasteboard, changeCountSelector);

        // 设置定时器
        _clipboardCheckTimer = new Timer(interval);
        _clipboardCheckTimer.Elapsed += CheckClipboard;
        _clipboardCheckTimer.Start();
    }

    private void CheckClipboard(object sender, ElapsedEventArgs e)
    {
        IntPtr changeCountSelector = sel_registerName("changeCount");
        int currentChangeCount = objc_msgSend_int(_pasteboard, changeCountSelector);

        if (currentChangeCount != _lastChangeCount)
        {
            _lastChangeCount = currentChangeCount;
            ClipboardChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _clipboardCheckTimer.Stop();
        _clipboardCheckTimer.Dispose();
    }
}