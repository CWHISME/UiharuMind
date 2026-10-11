/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using static UiharuMind.Core.AI.Execution.Tools.Desktop.Mac.MacNative;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;

/// <summary>
/// 持有一个 AXUIElement：快照节点的 <c>Source</c> 就是它，动作时据此找回真实元素。
/// 快照一换旧树就没人引用，由终结器释放句柄
/// </summary>
internal sealed class MacAxElement
{
    private MacAxElement(IntPtr handle, string role, bool inWebArea)
    {
        Handle = handle;
        Role = role;
        InWebArea = inWebArea;
    }

    ~MacAxElement()
    {
        if (Handle != IntPtr.Zero) CFRelease(Handle);
    }

    /// <summary>AXUIElementRef（本对象持有一份引用）</summary>
    public IntPtr Handle { get; }

    /// <summary>原生角色（AXButton…）</summary>
    public string Role { get; }

    /// <summary>是否在网页内容里：Chromium 系对网页元素的 AXPress 会谎报成功，点击改走事件</summary>
    public bool InWebArea { get; }

    /// <summary>
    /// 接手一个已持有的引用（Create/Copy 规则得来的）
    /// </summary>
    /// <param name="owned">句柄</param>
    /// <param name="role">原生角色</param>
    /// <param name="inWebArea">是否在网页内容里</param>
    /// <returns>元素；空句柄为 null</returns>
    public static MacAxElement? Adopt(IntPtr owned, string role = "", bool inWebArea = false) =>
        owned == IntPtr.Zero ? null : new MacAxElement(owned, role, inWebArea);

    /// <summary>
    /// 另持一份引用（Get 规则得来的，如数组里的项）
    /// </summary>
    /// <param name="borrowed">句柄</param>
    /// <param name="role">原生角色</param>
    /// <param name="inWebArea">是否在网页内容里</param>
    /// <returns>元素</returns>
    public static MacAxElement Retain(IntPtr borrowed, string role = "", bool inWebArea = false) =>
        new(CFRetain(borrowed), role, inWebArea);

    /// <summary>
    /// 读一个属性，转成 .NET 值
    /// </summary>
    /// <param name="attribute">属性名</param>
    /// <returns>值；没有为 null</returns>
    public object? Get(string attribute)
    {
        if (AXUIElementCopyAttributeValue(Handle, Cf(attribute), out IntPtr value) != AxSuccess) return null;
        try
        {
            return ToManaged(value);
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>
    /// 读一个属性，连同 AX 错误码：区分「元素真没了」（<see cref="AxErrorInvalidUIElement"/>）
    /// 和「应用忙/没响应」（AX 超时回 <see cref="AxErrorCannotComplete"/>）——判窗口关闭只认前者
    /// </summary>
    /// <param name="attribute">属性名</param>
    /// <returns>错误码与值；出错时值为 null（出错时返回的句柄不属于我们，不释放）</returns>
    public (int Error, object? Value) GetDetailed(string attribute)
    {
        int error = AXUIElementCopyAttributeValue(Handle, Cf(attribute), out IntPtr value);
        if (error != AxSuccess) return (error, null);
        try
        {
            return (AxSuccess, ToManaged(value));
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>
    /// 读一个元素型属性（如 AXFocusedWindow）
    /// </summary>
    /// <param name="attribute">属性名</param>
    /// <returns>元素；没有为 null</returns>
    public MacAxElement? GetElement(string attribute)
    {
        if (AXUIElementCopyAttributeValue(Handle, Cf(attribute), out IntPtr value) != AxSuccess) return null;
        if (IsElement(value)) return Adopt(value);
        CFRelease(value);
        return null;
    }

    /// <summary>
    /// 读一个元素数组属性（如 AXWindows、AXChildren）
    /// </summary>
    /// <param name="attribute">属性名</param>
    /// <returns>元素；没有为空</returns>
    public IReadOnlyList<MacAxElement> GetElements(string attribute)
    {
        if (AXUIElementCopyAttributeValue(Handle, Cf(attribute), out IntPtr value) != AxSuccess) return [];
        try
        {
            return ArrayItems(value).Where(IsElement).Select(item => Retain(item)).ToList();
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>
    /// 执行一个动作（AXPress、AXRaise…）
    /// </summary>
    /// <param name="action">动作名</param>
    /// <returns>AX 错误码</returns>
    public int Perform(string action) => AXUIElementPerformAction(Handle, Cf(action));

    /// <summary>
    /// 设一个属性
    /// </summary>
    /// <param name="attribute">属性名</param>
    /// <param name="value">CF 值（调用方持有）</param>
    /// <returns>AX 错误码</returns>
    public int Set(string attribute, IntPtr value) => AXUIElementSetAttributeValue(Handle, Cf(attribute), value);

    /// <summary>
    /// 设一个字符串属性
    /// </summary>
    /// <param name="attribute">属性名</param>
    /// <param name="text">内容</param>
    /// <returns>AX 错误码</returns>
    public int SetString(string attribute, string text)
    {
        IntPtr value = CreateString(text);
        try
        {
            return Set(attribute, value);
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>
    /// 属性能不能设
    /// </summary>
    /// <param name="attribute">属性名</param>
    /// <returns>能为 true</returns>
    public bool IsSettable(string attribute) =>
        AXUIElementIsAttributeSettable(Handle, Cf(attribute), out byte settable) == AxSuccess && settable != 0;

    /// <summary>
    /// 是不是同一个元素
    /// </summary>
    /// <param name="other">另一个</param>
    /// <returns>是为 true</returns>
    public bool SameAs(MacAxElement other) => CFEqual(Handle, other.Handle) != 0;
}
