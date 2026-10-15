using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 开机自启动：按平台写系统登录项。Windows 走 HKCU Run 键，macOS 写
/// LaunchAgents plist，Linux 写 XDG autostart desktop 文件。
/// 查询与写入失败一律吞掉返回 false/错误串，不抛给界面。
/// </summary>
public static class AutoLaunchManager
{
    public const string WindowsRunValueName = "UiharuMind";
    private const string WindowsRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public const string MacPlistLabel = "com.cwhisme.uiharumind";
    public const string MacPlistFileName = "com.cwhisme.uiharumind.plist";

    public const string LinuxDesktopFileName = "uiharumind.desktop";

    /// <summary>当前这台机器是否已登记自启动。读失败按未登记算</summary>
    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return IsWindowsEnabled();
            if (OperatingSystem.IsMacOS()) return IsMacEnabled(MacPlistPath());
            if (OperatingSystem.IsLinux()) return IsLinuxEnabled(LinuxDesktopFilePath());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            Log.Warning("AutoLaunch query failed: " + e.Message);
        }

        return false;
    }

    /// <summary>打开或关闭自启动。返回 false 时 <paramref name="error"/> 是给人看的原因</summary>
    /// <param name="enabled"> true 打开，false 关闭</param>
    /// <param name="error">失败原因，成功为 null</param>
    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            string? exePath = CurrentExecutablePath();
            if (enabled && string.IsNullOrWhiteSpace(exePath))
            {
                error = "无法确定程序路径";
                return false;
            }

            if (OperatingSystem.IsWindows()) SetWindowsEnabled(enabled, exePath!);
            else if (OperatingSystem.IsMacOS()) SetMacEnabled(MacPlistPath(), enabled, exePath!);
            else if (OperatingSystem.IsLinux()) SetLinuxEnabled(LinuxDesktopFilePath(), enabled, exePath!);
            else
            {
                error = "当前平台不支持";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// 启动时按配置期望补登录项，但只动「坏了的」：缺失就建，路径过期（目标已不存在，
    /// 如更新后换了目录）就修；指向另一个<b>有效</b>安装位置时不动——那可能是用户手改的，不跟他抢。
    /// 期望关则完全不动。跟 <see cref="TrySetEnabled"/> 的区别：那是用户在设置页亲手点的，无条件执行。
    /// </summary>
    /// <param name="desiredEnabled">配置里的期望值</param>
    public static void SyncFromConfig(bool desiredEnabled)
    {
        if (!desiredEnabled) return;
        try
        {
            string? exePath = CurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                Log.Warning("AutoLaunch sync skipped: executable path is empty.");
                return;
            }

            if (OperatingSystem.IsWindows()) EnsureWindowsEntry(exePath);
            else if (OperatingSystem.IsMacOS())
            {
                // 开发期裸 exe 不在 .app 包里：它的路径随构建目录变，且直跑包内 exe 会丢 TCC 身份。
                // 此时写进真实 home 的登录项，会把生产项污染成开发路径，所以整步跳过
                if (ResolveMacBundlePath(exePath) is null)
                {
                    Log.Debug("AutoLaunch sync skipped: running outside an app bundle.");
                    return;
                }

                EnsureMacEntry(MacPlistPath(), exePath);
            }
            else if (OperatingSystem.IsLinux()) EnsureLinuxEntry(LinuxDesktopFilePath(), exePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            Log.Warning("AutoLaunch sync failed: " + e.Message);
        }
    }

    /// <summary>补登录项的几种结局：只有 Created/Repaired 会写盘</summary>
    public enum SyncDecision
    {
        UpToDate,
        Created,
        Repaired,
        LeftAlone,
    }

    /// <summary>
    /// 补写决策纯函数：缺失就建；已登记且就是当前程序就不动；登记的是别的路径时，
    /// 只有旧目标已不存在（过期）才修——旧目标还有效说明有人手改过，不覆盖。
    /// </summary>
    /// <param name="registeredExe">登录项里登记的程序路径，缺失为 null</param>
    /// <param name="currentExe">当前程序路径（macOS 传包路径，见 <see cref="MacIdentity"/>）</param>
    /// <param name="registeredTargetExists">登记的那个目标在盘上是否还存在</param>
    /// <param name="comparison">路径比对口径（Windows/macOS 不分大小写，Linux 分）</param>
    internal static SyncDecision DecideSync(
        string? registeredExe, string currentExe, bool registeredTargetExists, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(registeredExe)) return SyncDecision.Created;
        if (registeredExe.Equals(currentExe, comparison)) return SyncDecision.UpToDate;
        return registeredTargetExists ? SyncDecision.LeftAlone : SyncDecision.Repaired;
    }

    internal static bool TargetExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string? CurrentExecutablePath()
    {
        return Environment.ProcessPath
               ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsEnabled()
    {
        using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WindowsRunKeyPath, false);
        return key?.GetValue(WindowsRunValueName) is string value && value.Length > 0;
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsEnabled(bool enabled, string exePath)
    {
        using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WindowsRunKeyPath, true)
            ?? throw new IOException("打不开注册表 Run 键");
        if (enabled) key.SetValue(WindowsRunValueName, BuildWindowsCommand(exePath));
        else key.DeleteValue(WindowsRunValueName, false);
    }

    /// <summary>Run 键的命令行：路径加引号，含空格的安装目录不加会启动失败</summary>
    internal static string BuildWindowsCommand(string exePath) => "\"" + exePath + "\"";

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsRegisteredPath()
    {
        using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WindowsRunKeyPath, false);
        return key?.GetValue(WindowsRunValueName) is string value
            ? value.Trim().Trim('"')
            : null;
    }

    [SupportedOSPlatform("windows")]
    private static SyncDecision EnsureWindowsEntry(string exePath)
    {
        string? registered = ReadWindowsRegisteredPath();
        SyncDecision decision = DecideSync(registered, exePath,
            registered is not null && TargetExists(registered), StringComparison.OrdinalIgnoreCase);
        switch (decision)
        {
            case SyncDecision.Created:
            case SyncDecision.Repaired:
                SetWindowsEnabled(true, exePath);
                break;
            case SyncDecision.LeftAlone:
                Log.Debug("AutoLaunch sync left the Run key alone: " + registered);
                break;
            case SyncDecision.UpToDate:
                break;
        }

        return decision;
    }

    internal static string MacPlistPath(string? homeOverride = null)
    {
        string home = homeOverride
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "LaunchAgents", MacPlistFileName);
    }

    internal static bool IsMacEnabled(string plistPath) => File.Exists(plistPath);
    // 只看文件在不在：文件在但被 launchctl 临时卸载时也会显示已启用，可接受——下次登录仍会加载

    internal static void SetMacEnabled(string plistPath, bool enabled, string exePath)
    {
        if (!enabled)
        {
            if (File.Exists(plistPath)) File.Delete(plistPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
        File.WriteAllText(plistPath, BuildMacPlist(MacPlistLabel, BuildMacArguments(exePath)), Encoding.UTF8);
    }

    internal static SyncDecision EnsureMacEntry(string plistPath, string exePath)
    {
        string identity = MacIdentity(exePath);
        string? registered = ReadMacRegisteredPath(plistPath);
        SyncDecision decision = DecideSync(registered, identity,
            registered is not null && TargetExists(registered), StringComparison.OrdinalIgnoreCase);
        switch (decision)
        {
            case SyncDecision.Created:
            case SyncDecision.Repaired:
                SetMacEnabled(plistPath, true, exePath);
                break;
            case SyncDecision.LeftAlone:
                Log.Debug("AutoLaunch sync left the plist alone: " + registered);
                break;
            case SyncDecision.UpToDate:
                break;
        }

        return decision;
    }

    /// <summary>登录项里的身份：包里跑时是包路径（与 <see cref="BuildMacArguments"/> 一致），裸 exe 才是 exe 路径</summary>
    internal static string MacIdentity(string exePath) => ResolveMacBundlePath(exePath) ?? exePath;

    /// <summary>读 plist 里登记的启动目标。文件缺失为 null</summary>
    internal static string? ReadMacRegisteredPath(string plistPath)
    {
        if (!File.Exists(plistPath)) return null;
        List<string> args = [];
        string text = File.ReadAllText(plistPath);
        int arrayStart = text.IndexOf("<array>", StringComparison.Ordinal);
        int arrayEnd = text.IndexOf("</array>", StringComparison.Ordinal);
        if (arrayStart < 0 || arrayEnd < arrayStart) return string.Empty;
        int cursor = arrayStart;
        while (true)
        {
            int open = text.IndexOf("<string>", cursor, StringComparison.Ordinal);
            if (open < 0 || open > arrayEnd) break;
            int close = text.IndexOf("</string>", open, StringComparison.Ordinal);
            if (close < 0 || close > arrayEnd) break;
            // 写盘时转义过（见 BuildMacPlist），比对前先解回来，否则含 & 的路径永远对不上
            args.Add(System.Net.WebUtility.HtmlDecode(text[(open + "<string>".Length)..close]));
            cursor = close;
        }

        int dashA = args.IndexOf("-a");
        if (dashA >= 0 && dashA + 1 < args.Count) return args[dashA + 1];
        return args.Count > 0 ? args[0] : string.Empty;
    }

    /// <summary>
    /// 自启动命令：能定位到 .app 包就经 open 拉起整个包（TCC 授权钉在包身份上，直接跑包内 exe 身份不对），
    /// 开发期裸 exe（不在包里）才直接跑 exe。
    /// </summary>
    internal static IReadOnlyList<string> BuildMacArguments(string exePath)
    {
        if (ResolveMacBundlePath(exePath) is { } bundle)
            return ["/usr/bin/open", "-a", bundle];
        return [exePath];
    }

    /// <summary>从进程路径反推 .app 包路径。不在包里跑时为 null</summary>
    /// <param name="processPath">进程路径</param>
    internal static string? ResolveMacBundlePath(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath)) return null;
        int appIndex = processPath.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (appIndex >= 0) return processPath[..(appIndex + ".app".Length)];
        if (processPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return processPath;
        return null;
    }

    internal static string BuildMacPlist(string label, IReadOnlyList<string> programArguments)
    {
        StringBuilder args = new();
        foreach (string arg in programArguments)
            args.Append("        <string>").Append(SecurityElement.Escape(arg)).Append("</string>\n");
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
               + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
               + "<plist version=\"1.0\">\n<dict>\n"
               + "    <key>Label</key>\n"
               + "    <string>" + SecurityElement.Escape(label) + "</string>\n"
               + "    <key>RunAtLoad</key>\n"
               + "    <true/>\n"
               + "    <key>ProgramArguments</key>\n"
               + "    <array>\n"
               + args
               + "    </array>\n"
               + "</dict>\n</plist>\n";
    }

    internal static string LinuxDesktopFilePath(string? homeOverride = null)
    {
        string home = homeOverride
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "autostart", LinuxDesktopFileName);
    }

    internal static bool IsLinuxEnabled(string desktopFilePath)
    {
        if (!File.Exists(desktopFilePath)) return false;
        // 用户在桌面环境的自启动管理里关掉时，文件还在但带 Hidden=true
        foreach (string line in File.ReadLines(desktopFilePath))
        {
            if (line.Trim().Equals("Hidden=true", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    internal static void SetLinuxEnabled(string desktopFilePath, bool enabled, string exePath)
    {
        if (!enabled)
        {
            if (File.Exists(desktopFilePath)) File.Delete(desktopFilePath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(desktopFilePath)!);
        File.WriteAllText(desktopFilePath, BuildLinuxDesktopFile(exePath), Encoding.UTF8);
    }

    internal static SyncDecision EnsureLinuxEntry(string desktopFilePath, string exePath)
    {
        string? registered = ReadLinuxRegisteredPath(desktopFilePath);
        SyncDecision decision = DecideSync(registered, exePath,
            registered is not null && TargetExists(registered), StringComparison.Ordinal);
        switch (decision)
        {
            case SyncDecision.Created:
            case SyncDecision.Repaired:
                SetLinuxEnabled(desktopFilePath, true, exePath);
                break;
            case SyncDecision.LeftAlone:
                Log.Debug("AutoLaunch sync left the desktop file alone: " + registered);
                break;
            case SyncDecision.UpToDate:
                break;
        }

        return decision;
    }

    /// <summary>读 desktop 文件 Exec 键里的程序路径。文件缺失为 null</summary>
    internal static string? ReadLinuxRegisteredPath(string desktopFilePath)
    {
        if (!File.Exists(desktopFilePath)) return null;
        foreach (string line in File.ReadLines(desktopFilePath))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("Exec=", StringComparison.Ordinal)) continue;
            string value = trimmed["Exec=".Length..].Trim();
            string quoted;
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                // 结束引号是第一个前面没反斜杠的 "，转义的 \" 要跳过
                int close = -1;
                for (int i = 1; i < value.Length; i++)
                {
                    if (value[i] == '"' && value[i - 1] != '\\')
                    {
                        close = i;
                        break;
                    }
                }

                quoted = close > 0 ? value[1..close] : value[1..];
            }
            else
            {
                int space = value.IndexOf(' ');
                quoted = space > 0 ? value[..space] : value;
            }

            return quoted.Replace("\\\"", "\"").Replace("%%", "%");
        }

        return string.Empty;
    }

    internal static string BuildLinuxDesktopFile(string exePath)
    {
        return "[Desktop Entry]\n"
               + "Type=Application\n"
               + "Name=UiharuMind\n"
               + "Comment=UiharuMind\n"
               + "Exec=\"" + QuoteLinuxExecArg(exePath) + "\"\n"
               + "Terminal=false\n"
               + "X-GNOME-Autostart-enabled=true\n";
    }

    /// <summary>
    /// Exec 引号内的转义：<c>%</c> 是 XDG 字段码（<c>%f</c> 之类），字面量要写 <c>%%</c>；
    /// <c>"</c> 写 <c>\"</c>。直接删字符会损坏路径。
    /// </summary>
    internal static string QuoteLinuxExecArg(string exePath) =>
        exePath.Replace("%", "%%").Replace("\"", "\\\"");
}
