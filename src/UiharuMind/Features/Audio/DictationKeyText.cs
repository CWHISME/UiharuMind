using System;
using SharpHook.Data;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 按住说话那个键的存取与显示：存 KeyCode 名，显示按平台叫法（macOS 的 Alt 叫 Option、Meta 叫 Command），区分左右
/// </summary>
public static class DictationKeyText
{
    /// <summary>
    /// 从设置里的名字解析；认不出为 <see cref="KeyCode.VcUndefined"/>
    /// </summary>
    /// <param name="name">KeyCode 名</param>
    /// <returns>键</returns>
    public static KeyCode Parse(string? name) =>
        Enum.TryParse(name, out KeyCode key) ? key : KeyCode.VcUndefined;

    /// <summary>
    /// 给人看的名字，如「右 Option」「右 Ctrl」「F8」
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>名字</returns>
    public static string Describe(KeyCode key)
    {
        bool mac = OperatingSystem.IsMacOS();
        (bool? right, string? name) = key switch
        {
            KeyCode.VcLeftAlt => (false, mac ? "Option" : "Alt"),
            KeyCode.VcRightAlt => (true, mac ? "Option" : "Alt"),
            KeyCode.VcLeftControl => (false, mac ? "Control" : "Ctrl"),
            KeyCode.VcRightControl => (true, mac ? "Control" : "Ctrl"),
            KeyCode.VcLeftShift => (false, "Shift"),
            KeyCode.VcRightShift => (true, "Shift"),
            KeyCode.VcLeftMeta => (false, mac ? "Command" : "Win"),
            KeyCode.VcRightMeta => (true, mac ? "Command" : "Win"),
            _ => ((bool?)null, (string?)null)
        };
        if (name == null)
        {
            string raw = key.ToString();
            return raw.StartsWith("Vc", StringComparison.Ordinal) ? raw[2..] : raw;
        }

        return Loc.Text(right == true ? LangKey.DictationKeyRight : LangKey.DictationKeyLeft, name);
    }
}
