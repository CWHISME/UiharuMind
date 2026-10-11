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

    /// <summary>
    /// <c>desktop.open</c> 一个<b>应用名</b>之前的预检（ADR 0084）：名单内的应用不能开（开了也拍不了动不了，白开），
    /// 长得像路径或 URL 的不当应用名放行（open 只认应用名与工作区/草稿目录里的文件，免得成了绕过 Shell 审批的口子）
    /// </summary>
    /// <param name="target">模型写的应用名</param>
    /// <param name="blockedApps">拒绝名单；为 null 用默认的</param>
    /// <returns>拒绝原因（写给模型看）；能开为 null</returns>
    public static string? ValidateAppOpen(string target, IReadOnlyList<string>? blockedApps = null)
    {
        string name = target.Trim();
        if (name.Length == 0) return "nothing to open";
        if (LooksLikePathTarget(name)) return "that looks like a file path, not an app name";
        if (LooksLikeUri(name)) return "that looks like a URL, not an app name";
        string wanted = NormalizeAppName(name);
        if ((blockedApps ?? DefaultBlockedApps).Any(app => AppForbidden(wanted, NormalizeAppName(app))))
        {
            return "it is on the off-limits list (passwords, system settings or a terminal); ask the user to do it";
        }

        return null;
    }

    /// <summary>
    /// <c>desktop.open</c> 的文件分支入口判断（ADR 0084）：只认工作区/草稿目录里的文件；
    /// 没有路径解析器（<c>_paths == null</c>）时不能把任意路径原样交给平台当应用名开——Windows 会当真执行
    /// </summary>
    /// <param name="target">模型给的 target</param>
    /// <param name="hasPathResolver">宿主是否带了路径解析器（工作区/草稿口径）</param>
    /// <returns>这一层就能判的错；不是路径（当应用名处理）或可以继续解析为 null</returns>
    public static string? FileTargetError(string target, bool hasPathResolver)
    {
        if (!LooksLikePathTarget(target.Trim())) return null;
        return hasPathResolver ? null : "file paths are not supported here; open an app by name instead";
    }

    /// <summary>
    /// open 的 target 是不是路径形状（应用名不该有这些特征）
    /// </summary>
    /// <param name="target">target</param>
    /// <returns>是为 true</returns>
    public static bool LooksLikePathTarget(string target) =>
        // 已知取舍：Path.GetExtension 会把带点的合法应用名（zoom.us、diagrams.net）也当路径拒掉；
        // 安全优先接受误伤，真误伤再收窄成可执行/脚本扩展名集合
        target.Contains('/') || target.Contains('\\') || target.StartsWith('$') || target.StartsWith('.')
        || Path.GetExtension(target).Length > 0;

    // 名单条目 -> 可比对的简短名：bundle id 取最后一段（com.apple.Terminal -> Terminal），可执行名去 .exe；
    // 去空格、小写，与模型写的名字在同一坐标系比。bundle id 不经过扩展名切分——
    // Path.GetFileNameWithoutExtension("com.apple.Terminal") 会把 Terminal 当扩展名
    private static string NormalizeAppName(string value)
    {
        string baseName = GetFileNameOrBundleTail(value);
        return baseName.Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static string GetFileNameOrBundleTail(string value)
    {
        if (IsBundleId(value)) return value[(value.LastIndexOf('.') + 1)..];
        return Path.GetFileNameWithoutExtension(value);
    }

    // target 是名单条目的简写（Terminal 配 com.apple.Terminal），或条目是 target 的简写（uiharumind.desktop 配 UiharuMind）
    private static bool AppForbidden(string wanted, string entry) =>
        // 宽匹配是故意（安全优先：Terminal↔com.apple.Terminal、UiharuMind↔UiharuMind.Desktop 靠它命中）；
        // 代价是前缀相近的不同应用会误伤（keepass↔keepassxc、cmder↔cmd），真误伤再收窄成别名表
        wanted.Length > 0 && entry.Length > 0 && (wanted == entry || entry.StartsWith(wanted, StringComparison.Ordinal)
                                                  || wanted.StartsWith(entry, StringComparison.Ordinal));

    // 对 DesktopHost 的文件分支预检（isFile 直传时 URL 不能当真文件开）公开
    public static bool LooksLikeUri(string name) =>
        name.Contains("://") || (name.Contains(':') && Uri.TryCreate(name, UriKind.Absolute, out Uri? uri) && uri.Scheme.Length > 1);

    private static bool IsBundleId(string value) =>
        value.StartsWith("com.", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("org.", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("net.", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("dev.", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("co.", StringComparison.OrdinalIgnoreCase);
}
