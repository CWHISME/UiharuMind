using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UiharuMind.Core.Core;

/// <summary>
/// 开发者模式：开着时应用开本机控制通道（ADR 0059）。
/// 与屏蔽角色的解锁（<c>CharacterVisibility</c>）各是一个标记，互不牵连；入口同在导入角色窗口的链接框里。
/// 口令不明文进代码，只存加盐后的 SHA-256
/// </summary>
public static class DeveloperMode
{
    private const string Salt = "UiharuMind.DeveloperMode:";
    private const string PassphraseHash = "6d93cc535f8865c50b380b6517841bda5b33b7ffd0a17af16ad37d5aa13cbafc";

    private static readonly MarkerFileFlag Flag = new(Path.Combine(AppPaths.Data.Root, "DeveloperMode"));

    /// <summary>开发者模式是否开着</summary>
    public static bool IsEnabled => Flag.IsSet;

    /// <summary>开 / 关时触发</summary>
    public static event Action? Changed
    {
        add => Flag.Changed += value;
        remove => Flag.Changed -= value;
    }

    /// <summary>
    /// 开 / 关开发者模式并落盘
    /// </summary>
    /// <param name="value">True 打开</param>
    public static void SetEnabled(bool value) => Flag.Set(value);

    /// <summary>
    /// 输入与口令对得上就打开开发者模式
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <returns>对上并已打开为 true</returns>
    public static bool TryUnlock(string? input)
    {
        if (!Matches(input)) return false;
        SetEnabled(true);
        return true;
    }

    /// <summary>
    /// 输入是否就是口令（首尾空白不算，大小写算）
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <returns>对得上为 true</returns>
    internal static bool Matches(string? input) => Matches(input, PassphraseHash);

    /// <summary>
    /// 输入加盐哈希后是否等于给定哈希（测试用自己的口令，仓库开源，真口令不进测试）
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <param name="expectedHash">十六进制 SHA-256</param>
    /// <returns>对得上为 true</returns>
    internal static bool Matches(string? input, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        return CryptographicOperations.FixedTimeEquals(Hash(input.Trim()), Convert.FromHexString(expectedHash));
    }

    /// <summary>加盐 SHA-256</summary>
    /// <param name="passphrase">口令</param>
    /// <returns>哈希</returns>
    internal static byte[] Hash(string passphrase) => SHA256.HashData(Encoding.UTF8.GetBytes(Salt + passphrase));
}
