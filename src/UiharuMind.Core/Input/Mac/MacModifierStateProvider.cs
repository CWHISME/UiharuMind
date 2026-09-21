using System.Runtime.InteropServices;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input.Mac;

/// <summary>
/// macOS 的修饰键真值查询，走 CoreGraphics 直接读事件源状态。
/// <para>
/// 与 <see cref="MacPointerLocator"/> 同理：钩子给的是「事件来过之后」的快照，
/// 而这里读的是此刻的硬件状态，任何丢事件的路径都绕不过它。
/// </para>
/// </summary>
public sealed class MacModifierStateProvider : IModifierStateProvider
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// kCGEventSourceStateCombinedSessionState：整个登录会话的合并状态，即用户实际按着的键
    private const int CombinedSessionState = 0;

    // 设备相关位（IOKit 的 NX_DEVICE*KEYMASK），能区分左右
    private const ulong DeviceLeftCtrl = 0x00000001;
    private const ulong DeviceLeftShift = 0x00000002;
    private const ulong DeviceRightShift = 0x00000004;
    private const ulong DeviceLeftMeta = 0x00000008;
    private const ulong DeviceRightMeta = 0x00000010;
    private const ulong DeviceLeftAlt = 0x00000020;
    private const ulong DeviceRightAlt = 0x00000040;
    private const ulong DeviceRightCtrl = 0x00002000;

    // 通用位（kCGEventFlagMask*），不分左右，作为设备位缺失时的兜底
    private const ulong GenericShift = 0x00020000;
    private const ulong GenericCtrl = 0x00040000;
    private const ulong GenericAlt = 0x00080000;
    private const ulong GenericMeta = 0x00100000;

    private bool _isAvailable = true;

    public bool IsAvailable => _isAvailable;

    public EModifierKeys GetPressedModifiers()
    {
        if (!_isAvailable) return EModifierKeys.None;

        ulong flags;
        try
        {
            flags = CGEventSourceFlagsState(CombinedSessionState);
        }
        catch (Exception e)
        {
            // 取不到就永久退回事件位集，不必每次调用都再撞一次原生边界
            _isAvailable = false;
            Log.Warning($"Failed to query macOS modifier state: {e.Message}");
            return EModifierKeys.None;
        }

        var modifiers = EModifierKeys.None;
        modifiers |= ResolveSide(flags, GenericShift, DeviceLeftShift, DeviceRightShift,
            EModifierKeys.LeftShift, EModifierKeys.RightShift);
        modifiers |= ResolveSide(flags, GenericCtrl, DeviceLeftCtrl, DeviceRightCtrl,
            EModifierKeys.LeftCtrl, EModifierKeys.RightCtrl);
        modifiers |= ResolveSide(flags, GenericAlt, DeviceLeftAlt, DeviceRightAlt,
            EModifierKeys.LeftAlt, EModifierKeys.RightAlt);
        modifiers |= ResolveSide(flags, GenericMeta, DeviceLeftMeta, DeviceRightMeta,
            EModifierKeys.LeftMeta, EModifierKeys.RightMeta);
        return modifiers;
    }

    /// 设备位能分左右就照实报；只有通用位时无从判断，按左侧计（快捷键匹配会折叠左右，不受影响）
    private static EModifierKeys ResolveSide(ulong flags, ulong genericMask, ulong leftMask, ulong rightMask,
        EModifierKeys left, EModifierKeys right)
    {
        var result = EModifierKeys.None;
        if ((flags & leftMask) != 0) result |= left;
        if ((flags & rightMask) != 0) result |= right;
        if (result == EModifierKeys.None && (flags & genericMask) != 0) result = left;
        return result;
    }

    [DllImport(CoreGraphics)]
    private static extern ulong CGEventSourceFlagsState(int stateId);
}
