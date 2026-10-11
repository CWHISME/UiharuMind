/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 哪些窗口不让看、不让动（ADR 0084）：密码管理、系统设置、终端、本应用自身，以及浏览器工具自己开的那个 Chrome。
/// 名单里的窗口照样列出（模型知道它在），只是拍不了、动不了
/// </summary>
public static class DesktopPolicy
{
    /// <summary>
    /// 默认拒绝的应用标识（macOS bundle id、Windows 可执行文件名，不分大小写）
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultBlockedApps =
    [
        // 密码与凭据
        "com.apple.keychainaccess", "com.apple.Passwords", "com.1password.1password", "com.bitwarden.desktop",
        "1Password.exe", "Bitwarden.exe", "KeePass.exe", "KeePassXC.exe", "CredentialUIBroker.exe",
        // 系统设置
        "com.apple.systempreferences", "SystemSettings.exe", "mmc.exe", "regedit.exe",
        // 系统的授权与密码弹窗：替用户点「允许」就绕过了系统那道门
        "com.apple.accessibility.universalAccessAuthWarn", "com.apple.SecurityAgent", "com.apple.coreautha",
        "consent.exe",
        // 终端：能做的事等于 shell，而 shell 有自己的审批
        "com.apple.Terminal", "com.googlecode.iterm2", "com.mitchellh.ghostty", "dev.warp.Warp-Stable",
        "net.kovidgoyal.kitty", "org.alacritty", "com.github.wez.wezterm", "co.zeit.hyper", "org.tabby",
        "WindowsTerminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "wt.exe",
        // 本应用的其他实例
        "com.cwhisme.uiharumind", "UiharuMind.Desktop", "UiharuMind.Desktop.exe",
    ];

    /// <summary>
    /// 这个窗口为什么不能碰
    /// </summary>
    /// <param name="window">窗口</param>
    /// <param name="ownBrowser">某进程是不是浏览器工具开的 Chrome</param>
    /// <param name="blockedApps">拒绝名单；为 null 用默认的</param>
    /// <returns>原因（写给模型看）；能碰为 null</returns>
    public static string? Blocked(DesktopWindowInfo window, Func<int, bool>? ownBrowser = null,
        IReadOnlyList<string>? blockedApps = null)
    {
        if (window.Pid == Environment.ProcessId) return "it belongs to this assistant app itself";
        if (ownBrowser?.Invoke(window.Pid) == true)
        {
            return "it is the browser that the browser extension controls; use the browser extension for it";
        }

        return (blockedApps ?? DefaultBlockedApps).Any(app => string.Equals(app, window.AppId, StringComparison.OrdinalIgnoreCase))
            ? "the user keeps this app off limits (passwords, system settings or a terminal); ask the user to do it"
            : null;
    }
}
