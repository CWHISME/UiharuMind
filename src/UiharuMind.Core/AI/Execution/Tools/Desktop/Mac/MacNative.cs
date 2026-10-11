/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;

/// <summary>
/// macOS 无障碍（AX）、CoreFoundation、CoreGraphics 的 C 接口，外加 CF 值与 .NET 值的互转。
/// 全是公开接口；C 的 Boolean 是一个字节，返回值一律按 byte 收，免得默认的 4 字节 BOOL 封送读出脏位
/// </summary>
internal static class MacNative
{
    public const int AxSuccess = 0;
    public const int AxErrorCannotComplete = -25204;

    public const int AxValueCgPoint = 1;
    public const int AxValueCgSize = 2;
    public const int AxValueAxError = 5;

    public const uint EventLeftMouseDown = 1;
    public const uint EventLeftMouseUp = 2;
    public const uint EventRightMouseDown = 3;
    public const uint EventRightMouseUp = 4;
    public const uint EventMouseMoved = 5;
    public const uint MouseButtonLeft = 0;
    public const uint MouseButtonRight = 1;
    public const uint FieldMouseClickState = 1;
    public const uint FieldWindowUnderPointer = 91; //投递给后台窗口时指明是哪个窗口，应用才知道点的是谁
    public const uint FieldWindowUnderPointerThatCanHandle = 92;
    public const int EventSourcePrivate = -1; //这里发的事件不算进「用户最近一次输入」
    public const int EventSourceHidSystem = 1;
    public const uint AnyInputEvent = 0xFFFFFFFF;
    public const uint HidEventTap = 0;
    public const uint ScrollUnitLine = 1;

    public const uint WindowListOptionAll = 0;
    public const uint WindowListExcludeDesktop = 16;

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int NumberDoubleType = 13;

    private static readonly ConcurrentDictionary<string, IntPtr> Strings = new(); //属性名之类的常量串，进程内常驻
    private static readonly Lazy<IntPtr> CoreFoundationHandle = new(() => NativeLibrary.Load(CoreFoundation));
    private static readonly Lazy<IntPtr> ApplicationServicesHandle = new(() => NativeLibrary.Load(ApplicationServices));

    /// <summary>
    /// 常量 CFString（缓存，不释放）
    /// </summary>
    /// <param name="text">内容</param>
    /// <returns>CFStringRef</returns>
    public static IntPtr Cf(string text) => Strings.GetOrAdd(text, CreateString);

    /// <summary>
    /// 新建 CFString，调用方负责 <see cref="CFRelease"/>
    /// </summary>
    /// <param name="text">内容</param>
    /// <returns>CFStringRef</returns>
    public static IntPtr CreateString(string text) => CFStringCreateWithCharacters(IntPtr.Zero, text, text.Length);

    /// <summary>
    /// CF 值转成 .NET 值：串、布尔、数、点与尺寸、URL；元素与数组原样给句柄（不转移所有权）
    /// </summary>
    /// <param name="value">CF 值</param>
    /// <returns>.NET 值；认不出或是 AX 错误占位为 null</returns>
    public static object? ToManaged(IntPtr value)
    {
        if (value == IntPtr.Zero) return null;
        nuint type = CFGetTypeID(value);
        if (type == CFStringGetTypeID()) return ReadString(value);
        if (type == CFBooleanGetTypeID()) return CFBooleanGetValue(value) != 0;
        if (type == CFNumberGetTypeID()) return CFNumberGetValue(value, NumberDoubleType, out double number) != 0 ? number : null;
        if (type == CFURLGetTypeID()) return ReadString(CFURLGetString(value));
        if (type == AXValueGetTypeID())
        {
            int kind = AXValueGetType(value);
            if ((kind == AxValueCgPoint || kind == AxValueCgSize) && AXValueGetValue(value, kind, out CGPoint pair) != 0)
            {
                return pair;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// 读 CFString
    /// </summary>
    /// <param name="value">CFStringRef</param>
    /// <returns>内容；空句柄为空串</returns>
    public static string ReadString(IntPtr value)
    {
        if (value == IntPtr.Zero) return string.Empty;
        int length = (int)CFStringGetLength(value);
        if (length == 0) return string.Empty;
        char[] buffer = new char[length];
        CFStringGetCharacters(value, new CFRange(0, length), buffer);
        return new string(buffer);
    }

    /// <summary>是否 CFArray</summary>
    public static bool IsArray(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFArrayGetTypeID();

    /// <summary>是否 AXUIElement</summary>
    public static bool IsElement(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == AXUIElementGetTypeID();

    /// <summary>
    /// 取数组里的各项（不转移所有权）
    /// </summary>
    /// <param name="array">CFArrayRef</param>
    /// <returns>各项句柄</returns>
    public static IntPtr[] ArrayItems(IntPtr array)
    {
        if (!IsArray(array)) return [];
        int count = (int)CFArrayGetCount(array);
        IntPtr[] items = new IntPtr[count];
        for (int i = 0; i < count; i++) items[i] = CFArrayGetValueAtIndex(array, i);
        return items;
    }

    /// <summary>
    /// 新建 CFArray（元素按 CF 规则持有），调用方负责释放
    /// </summary>
    /// <param name="items">元素</param>
    /// <returns>CFArrayRef</returns>
    public static IntPtr CreateArray(IntPtr[] items) =>
        CFArrayCreate(IntPtr.Zero, items, items.Length, NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFTypeArrayCallBacks"));

    /// <summary>CFBoolean 的真值</summary>
    public static IntPtr True => Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFBooleanTrue"));

    /// <summary>CFBoolean 的假值</summary>
    public static IntPtr False => Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFBooleanFalse"));

    /// <summary>
    /// 带「弹出授权框」选项问一次是否已授权无障碍
    /// </summary>
    /// <returns>已授权为 true</returns>
    public static bool PromptForTrust()
    {
        IntPtr key = Marshal.ReadIntPtr(NativeLibrary.GetExport(ApplicationServicesHandle.Value, "kAXTrustedCheckOptionPrompt"));
        IntPtr options = CFDictionaryCreate(IntPtr.Zero, [key], [True], 1,
            NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(CoreFoundationHandle.Value, "kCFTypeDictionaryValueCallBacks"));
        try
        {
            return AXIsProcessTrustedWithOptions(options) != 0;
        }
        finally
        {
            CFRelease(options);
        }
    }

    /// <summary>
    /// 进程的可执行文件路径
    /// </summary>
    /// <param name="pid">进程 id</param>
    /// <returns>路径；取不到为空串</returns>
    public static string ProcessPath(int pid)
    {
        byte[] buffer = new byte[4096];
        int length = proc_pidpath(pid, buffer, (uint)buffer.Length);
        return length > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, length) : string.Empty;
    }

    /// <summary>
    /// 一个 .app 包的 bundle id
    /// </summary>
    /// <param name="bundlePath">包的路径</param>
    /// <returns>bundle id；取不到为 null</returns>
    public static string? BundleIdentifier(string bundlePath)
    {
        byte[] path = System.Text.Encoding.UTF8.GetBytes(bundlePath);
        IntPtr url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, 1);
        if (url == IntPtr.Zero) return null;
        try
        {
            IntPtr bundle = CFBundleCreate(IntPtr.Zero, url);
            if (bundle == IntPtr.Zero) return null;
            try
            {
                string id = ReadString(CFBundleGetIdentifier(bundle));
                return id.Length > 0 ? id : null;
            }
            finally
            {
                CFRelease(bundle);
            }
        }
        finally
        {
            CFRelease(url);
        }
    }

    /// <summary>
    /// 字典里按键取值（不转移所有权）
    /// </summary>
    /// <param name="dictionary">CFDictionaryRef</param>
    /// <param name="key">键</param>
    /// <returns>值句柄</returns>
    public static IntPtr DictionaryValue(IntPtr dictionary, string key) => CFDictionaryGetValue(dictionary, Cf(key));

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint(double x, double y)
    {
        public double X = x;
        public double Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CFRange(nint location, nint length)
    {
        public readonly nint Location = location;
        public readonly nint Length = length;
    }

    // ---- Accessibility ----

    [DllImport(ApplicationServices)]
    public static extern byte AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    private static extern byte AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(ApplicationServices)]
    public static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport(ApplicationServices)]
    public static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(ApplicationServices)]
    public static extern nuint AXUIElementGetTypeID();

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementGetPid(IntPtr element, out int pid);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementCopyMultipleAttributeValues(IntPtr element, IntPtr attributes, int options,
        out IntPtr values);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementIsAttributeSettable(IntPtr element, IntPtr attribute, out byte settable);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementPerformAction(IntPtr element, IntPtr action);

    [DllImport(ApplicationServices)]
    public static extern int AXUIElementSetMessagingTimeout(IntPtr element, float timeoutInSeconds);

    [DllImport(ApplicationServices)]
    private static extern nuint AXValueGetTypeID();

    [DllImport(ApplicationServices)]
    private static extern int AXValueGetType(IntPtr value);

    [DllImport(ApplicationServices)]
    private static extern byte AXValueGetValue(IntPtr value, int type, out CGPoint pair);

    // ---- CoreFoundation ----

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr value);

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFRetain(IntPtr value);

    [DllImport(CoreFoundation)]
    public static extern byte CFEqual(IntPtr a, IntPtr b);

    [DllImport(CoreFoundation)]
    private static extern nuint CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFArrayGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFNumberGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern nuint CFURLGetTypeID();

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, string chars, nint length);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr value);

    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)]
    private static extern void CFStringGetCharacters(IntPtr value, CFRange range, [Out] char[] buffer);

    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr[] values, nint count, IntPtr callbacks);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count,
        IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundation)]
    private static extern byte CFBooleanGetValue(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern byte CFNumberGetValue(IntPtr number, int type, out double value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFURLGetString(IntPtr url);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte[] path, nint length,
        byte isDirectory);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFBundleCreate(IntPtr allocator, IntPtr url);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFBundleGetIdentifier(IntPtr bundle);

    // ---- CoreGraphics ----

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphics)]
    public static extern byte CGPreflightScreenCaptureAccess();

    [DllImport(CoreGraphics)]
    public static extern byte CGRequestScreenCaptureAccess();

    [DllImport(CoreGraphics)]
    public static extern byte CGPreflightPostEventAccess();

    [DllImport(CoreGraphics)]
    public static extern byte CGRequestPostEventAccess();

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventSourceCreate(int stateId);

    [DllImport(CoreGraphics)]
    public static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint type, CGPoint position, uint button);

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, byte keyDown);

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, uint units, uint wheelCount, int wheel1,
        int wheel2, int wheel3);

    [DllImport(CoreGraphics, CharSet = CharSet.Unicode)]
    public static extern void CGEventKeyboardSetUnicodeString(IntPtr cgEvent, nuint length, char[] chars);

    [DllImport(CoreGraphics)]
    public static extern void CGEventSetFlags(IntPtr cgEvent, ulong flags);

    [DllImport(CoreGraphics)]
    public static extern void CGEventSetLocation(IntPtr cgEvent, CGPoint location);

    [DllImport(CoreGraphics)]
    public static extern void CGEventSetIntegerValueField(IntPtr cgEvent, uint field, long value);

    [DllImport(CoreGraphics)]
    public static extern void CGEventPostToPid(int pid, IntPtr cgEvent);

    [DllImport(CoreGraphics)]
    public static extern void CGEventPost(uint tap, IntPtr cgEvent);

    // ---- libproc ----

    [DllImport("/usr/lib/libproc.dylib")]
    private static extern int proc_pidpath(int pid, byte[] buffer, uint bufferSize);
}
