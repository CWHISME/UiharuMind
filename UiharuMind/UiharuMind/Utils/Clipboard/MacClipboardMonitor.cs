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

using System;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace UiharuMind.Utils.Clipboard;

/// <summary>
/// 听说 mac 没有直接监听剪贴板的方法，只能通过定时器来检测剪贴板变化
/// </summary>
public class MacClipboardMonitor : IClipboardMonitor
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjCLibrary)]
    private static extern IntPtr objc_getClass(string className);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);

    private static readonly IntPtr GeneralPasteboardSelector = sel_registerName("generalPasteboard");
    private static readonly IntPtr ChangeCountSelector = sel_registerName("changeCount");

    private readonly DispatcherTimer _clipboardCheckTimer;
    private readonly IntPtr _pasteboard;
    private nint _lastChangeCount;
    private bool _disposed;
    private bool _isChecking;

    public event Action? OnClipboardChanged;

    public MacClipboardMonitor(double interval = 1000)
    {
        IntPtr nsPasteboardClass = objc_getClass("NSPasteboard");
        _pasteboard = objc_msgSend_IntPtr(nsPasteboardClass, GeneralPasteboardSelector);

        _lastChangeCount = objc_msgSend_nint(_pasteboard, ChangeCountSelector);

        _clipboardCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(interval)
        };
        _clipboardCheckTimer.Tick += CheckClipboard;
        _clipboardCheckTimer.Start();
    }

    private void CheckClipboard(object? sender, EventArgs e)
    {
        if (_disposed || _isChecking) return;

        _isChecking = true;
        try
        {
            nint currentChangeCount = objc_msgSend_nint(_pasteboard, ChangeCountSelector);

            if (currentChangeCount == _lastChangeCount) return;

            _lastChangeCount = currentChangeCount;
            if (!_disposed) OnClipboardChanged?.Invoke();
        }
        finally
        {
            _isChecking = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clipboardCheckTimer.Stop();
        _clipboardCheckTimer.Tick -= CheckClipboard;
        OnClipboardChanged = null;
        GC.SuppressFinalize(this);
    }
}
