/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 系统里装了哪些 Chromium 系浏览器（除了让 Puppeteer 自己系统检测的 Google Chrome）。
/// 第一版只认 Microsoft Edge；以后要接 Brave、Vivaldi 之类，在这里加探测方法、
/// 并把 <see cref="BrowserLauncher"/> 的补位顺序扩进去即可。
/// </summary>
internal static class BrowserLocator
{
    /// <summary>
    /// 按平台返回 Edge 可执行文件的候选路径；候选都真实存在时取第一个
    /// </summary>
    internal static string[] EdgeExecutableCandidates(EBrowserPlatform platform) => platform switch
    {
        // Edge 在 Windows 上默认装进 x86 的 Program Files；再兜 Program Files、
        // 用户级安装（LocalAppData）与最常见的写死路径
        EBrowserPlatform.Windows =>
        [
            .. WindowsProgramFilePrefixes().Select(prefix =>
                Path.Combine(prefix, "Microsoft", "Edge", "Application", "msedge.exe")),
        ],
        EBrowserPlatform.MacOS =>
        [
            "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
        ],
        EBrowserPlatform.Linux =>
        [
            // Debian/Ubuntu 的 .deb 包另提供 /usr/bin/microsoft-edge-stable（符号链接）
            "/opt/microsoft/msedge/msedge",
            "/usr/bin/microsoft-edge",
            "/usr/bin/microsoft-edge-stable",
        ],
        _ => [],
    };

    /// <summary>按当前平台返回候选路径</summary>
    internal static string[] EdgeExecutableCandidates() => EdgeExecutableCandidates(CurrentPlatform());

    /// <summary>系统里第一个真实存在的 Edge 可执行文件；一个都没有为 null</summary>
    internal static string? TryFindEdge() => EdgeExecutableCandidates().FirstOrDefault(File.Exists);

    internal static EBrowserPlatform CurrentPlatform() => OperatingSystem.IsWindows()
        ? EBrowserPlatform.Windows
        : OperatingSystem.IsMacOS()
            ? EBrowserPlatform.MacOS
            : EBrowserPlatform.Linux;

    private static string[] WindowsProgramFilePrefixes()
    {
        HashSet<string> prefixes = [];
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        prefixes.Add(@"C:\Program Files");
        prefixes.Add(@"C:\Program Files (x86)");
        return [.. prefixes];

        void Add(string? prefix)
        {
            if (!string.IsNullOrEmpty(prefix)) prefixes.Add(prefix);
        }
    }
}

/// <summary>桌面浏览器探测用的平台</summary>
internal enum EBrowserPlatform
{
    /// <summary>Windows</summary>
    Windows,
    /// <summary>macOS</summary>
    MacOS,
    /// <summary>Linux</summary>
    Linux,
}