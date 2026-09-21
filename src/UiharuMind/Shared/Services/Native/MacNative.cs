/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace UiharuMind.Shared.Services.Native;

/// <summary>
/// macOS 原生调用的唯一出口：Objective-C 运行时的裸 P/Invoke 都收在这里，
/// 上层服务只描述意图（抬层级、设 alpha、换菜单），不再各自声明 objc_msgSend。
/// </summary>
internal static class MacNative
{
    public const string ObjcLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// 取窗口的 NSWindow 句柄
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="nsWindow">NSWindow 指针</param>
    /// <returns>取到返回 True</returns>
    public static bool TryGetNsWindow(Window window, out IntPtr nsWindow)
    {
        nsWindow = IntPtr.Zero;
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return false;
        nsWindow = handle.Handle;
        return true;
    }

    /// <summary>NSApplication 单例；取不到返回 IntPtr.Zero</summary>
    public static IntPtr SharedApplication() =>
        SendPtr(GetClass("NSApplication"), Selector("sharedApplication"));

    [StructLayout(LayoutKind.Sequential)]
    public struct CocoaRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport(ObjcLibrary, EntryPoint = "sel_registerName")]
    public static extern IntPtr Selector(string selectorName);

    [DllImport(ObjcLibrary, EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string className);

    [DllImport(ObjcLibrary, EntryPoint = "object_getClass")]
    public static extern IntPtr GetObjectClass(IntPtr obj);

    [DllImport(ObjcLibrary, EntryPoint = "object_setClass")]
    public static extern IntPtr SetObjectClass(IntPtr obj, IntPtr cls);

    [DllImport(ObjcLibrary, EntryPoint = "objc_autoreleasePoolPush")]
    public static extern IntPtr AutoreleasePoolPush();

    [DllImport(ObjcLibrary, EntryPoint = "objc_autoreleasePoolPop")]
    public static extern void AutoreleasePoolPop(IntPtr pool);

    // 以下都是 objc_msgSend 的不同签名。名字按「返回什么 + 带什么参数」取，
    // 调用方只挑签名，不再各自声明
    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendPtrAnsiString(IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.LPStr)] string arg);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern nint SendNint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern ulong SendULongRet(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern byte SendByte(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendBoolRet(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendBoolRetLong(IntPtr receiver, IntPtr selector, long arg);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern void Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern void Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendBool(IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendDouble(IntPtr receiver, IntPtr selector, double value);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendLong(IntPtr receiver, IntPtr selector, long value);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendULong(IntPtr receiver, IntPtr selector, ulong value);

    [DllImport(ObjcLibrary, EntryPoint = "objc_msgSend")]
    public static extern void SendRect(IntPtr receiver, IntPtr selector, CocoaRect rect, byte display, byte animate);
}
