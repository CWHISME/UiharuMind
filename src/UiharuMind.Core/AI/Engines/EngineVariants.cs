using System.Runtime.InteropServices;

namespace UiharuMind.Core.AI.Engines;

/// <summary>
/// 引擎发布包按「系统 × 架构 × 变体」命名，各家顺序不同（llama.cpp 是 win-vulkan-x64，audio.cpp 是 windows-x64-vulkan），
/// 推荐哪个变体由各引擎自己定，这里只给公共的平台判定
/// </summary>
public static class EngineVariants
{
    /// <summary>
    /// 当前系统；不支持的系统为 null
    /// </summary>
    public static OSPlatform? CurrentPlatform =>
        OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsLinux() ? OSPlatform.Linux
        : null;

    /// <summary>
    /// 本机是不是这个引擎的推荐变体
    /// </summary>
    /// <param name="packageName">包名（或由包名得到的版本名）</param>
    /// <param name="marker">按系统与架构给出推荐变体在包名里的标记；不推荐任何变体为 null</param>
    /// <param name="atEnd">标记必须在结尾（cpu 不能把 cpu-portable 也认进来）</param>
    /// <returns>是推荐变体返回 true</returns>
    public static bool IsRecommended(string packageName, Func<OSPlatform, string, string?> marker, bool atEnd = false)
    {
        OSPlatform? platform = CurrentPlatform;
        return platform != null &&
               IsRecommended(packageName, platform.Value, RuntimeInformation.ProcessArchitecture, marker, atEnd);
    }

    /// <summary>
    /// 指定平台下是不是推荐变体
    /// </summary>
    /// <param name="packageName">包名</param>
    /// <param name="platform">操作系统</param>
    /// <param name="architecture">CPU 架构</param>
    /// <param name="marker">按系统与架构给出推荐变体的标记</param>
    /// <param name="atEnd">标记必须在结尾</param>
    /// <returns>是推荐变体返回 true</returns>
    public static bool IsRecommended(string packageName, OSPlatform platform, Architecture architecture,
        Func<OSPlatform, string, string?> marker, bool atEnd = false)
    {
        string? arch = ArchitectureToken(architecture);
        string? token = arch == null ? null : marker(platform, arch);
        if (string.IsNullOrEmpty(token)) return false;
        return atEnd
            ? packageName.EndsWith(token, StringComparison.OrdinalIgnoreCase)
            : packageName.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 包名里的架构写法
    /// </summary>
    /// <param name="architecture">CPU 架构</param>
    /// <returns>arm64 / x64；其他架构为 null</returns>
    public static string? ArchitectureToken(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => null
        };
    }
}
