using System;
using System.IO;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 屏蔽角色（<see cref="CharacterData.IsShielded"/>）的可见性闸门。
///
/// 默认关闭——屏蔽角色不出现在角色库与任何选择器里；由「开发者网址」解锁后可见。
///
/// 解锁状态<b>刻意不走常规配置</b>：不进设置页、不是 <c>TConfigBase</c>，只落一个标记文件，
/// 存在即解锁。这样普通用户看不到这个开关，而开发者解锁一次之后也不用每次重输网址。
/// </summary>
public static class CharacterVisibility
{
    /// <summary>开发者网址：输入它即静默解锁屏蔽角色（刻意做成不起眼的入口，不做成设置项）</summary>
    public const string DeveloperUrl = "https://wangjiaying.top";

    /// <summary>解锁标记文件；存在即「屏蔽角色可见」</summary>
    private static readonly string UnlockMarker = Path.Combine(AppPaths.Data.Root, "DeveloperUnlocked");

    private static bool? _showShielded;

    /// <summary>屏蔽角色此刻是否可见（首次访问读一次标记文件）</summary>
    public static bool ShowShielded => _showShielded ??= File.Exists(UnlockMarker);

    /// <summary>解锁 / 锁回屏蔽角色时触发。列表、选择器之类常驻视图靠它重建，而不是等角色数据变化</summary>
    public static event Action? ShowShieldedChanged;

    /// <summary>解锁 / 锁回屏蔽角色，并落盘（下次启动仍生效）</summary>
    /// <param name="value">True 解锁</param>
    public static void SetShowShielded(bool value)
    {
        if (_showShielded == value) return;

        _showShielded = value;
        try
        {
            if (value)
            {
                Directory.CreateDirectory(AppPaths.Data.Root);
                File.WriteAllText(UnlockMarker, DateTimeOffset.Now.ToString("O"));
            }
            else if (File.Exists(UnlockMarker))
            {
                File.Delete(UnlockMarker);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Toggle shielded characters failed: {e.Message}");
        }

        ShowShieldedChanged?.Invoke();
    }

    /// <summary>该角色此刻是否应当出现在列表 / 选择器里（只看屏蔽这一维；内部角色另有开关）</summary>
    public static bool PassesShield(CharacterData character) => !character.IsShielded || ShowShielded;

    /// <summary>输入等于开发者网址则解锁屏蔽角色；返回是否解锁</summary>
    /// <param name="input">用户输入的地址</param>
    public static bool TryUnlock(string? input)
    {
        if (!string.Equals(input?.Trim(), DeveloperUrl, StringComparison.OrdinalIgnoreCase)) return false;
        SetShowShielded(true);
        return true;
    }
}
