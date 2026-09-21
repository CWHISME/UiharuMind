using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 修饰键位集。左右分列以便如实回答「哪一侧按着」，快捷键匹配则统一折叠到语义档（见 <see cref="ModifierKeysExtensions.Fold"/>）。
/// </summary>
[Flags]
public enum EModifierKeys
{
    None = 0,

    LeftShift = 1 << 0,
    RightShift = 1 << 1,
    LeftCtrl = 1 << 2,
    RightCtrl = 1 << 3,
    LeftAlt = 1 << 4,
    RightAlt = 1 << 5,
    LeftMeta = 1 << 6,
    RightMeta = 1 << 7,

    Shift = LeftShift | RightShift,
    Ctrl = LeftCtrl | RightCtrl,
    Alt = LeftAlt | RightAlt,
    Meta = LeftMeta | RightMeta,

    All = Shift | Ctrl | Alt | Meta
}

public static class ModifierKeysExtensions
{
    /// <summary>
    /// 折叠左右侧：任一侧按下即视为该档修饰键按下。
    /// 折叠后两个位集可直接判等，于是「左 Ctrl 注册、右 Ctrl 触发」也能命中。
    /// </summary>
    /// <param name="modifiers">原始位集</param>
    /// <returns>折叠后的位集</returns>
    public static EModifierKeys Fold(this EModifierKeys modifiers)
    {
        var folded = EModifierKeys.None;
        if ((modifiers & EModifierKeys.Shift) != 0) folded |= EModifierKeys.Shift;
        if ((modifiers & EModifierKeys.Ctrl) != 0) folded |= EModifierKeys.Ctrl;
        if ((modifiers & EModifierKeys.Alt) != 0) folded |= EModifierKeys.Alt;
        if ((modifiers & EModifierKeys.Meta) != 0) folded |= EModifierKeys.Meta;
        return folded;
    }

    /// <summary>
    /// 取键码对应的修饰键位，非修饰键返回 <see cref="EModifierKeys.None"/>
    /// </summary>
    /// <param name="keyCode">键码</param>
    /// <returns>对应位，非修饰键为 None</returns>
    public static EModifierKeys ToModifier(this KeyCode keyCode)
    {
        return keyCode switch
        {
            KeyCode.VcLeftShift => EModifierKeys.LeftShift,
            KeyCode.VcRightShift => EModifierKeys.RightShift,
            KeyCode.VcLeftControl => EModifierKeys.LeftCtrl,
            KeyCode.VcRightControl => EModifierKeys.RightCtrl,
            KeyCode.VcLeftAlt => EModifierKeys.LeftAlt,
            KeyCode.VcRightAlt => EModifierKeys.RightAlt,
            KeyCode.VcLeftMeta => EModifierKeys.LeftMeta,
            KeyCode.VcRightMeta => EModifierKeys.RightMeta,
            _ => EModifierKeys.None
        };
    }

    /// <summary>
    /// 是否为修饰键
    /// </summary>
    /// <param name="keyCode">键码</param>
    /// <returns>是修饰键返回 True</returns>
    public static bool IsModifier(this KeyCode keyCode) => keyCode.ToModifier() != EModifierKeys.None;

    /// <summary>
    /// 把一组修饰键键码合并成位集
    /// </summary>
    /// <param name="keyCodes">修饰键键码，含非修饰键时忽略之</param>
    /// <returns>合并后的位集</returns>
    public static EModifierKeys ToModifiers(this IEnumerable<KeyCode>? keyCodes)
    {
        if (keyCodes == null) return EModifierKeys.None;
        var modifiers = EModifierKeys.None;
        foreach (var keyCode in keyCodes) modifiers |= keyCode.ToModifier();
        return modifiers;
    }
}
