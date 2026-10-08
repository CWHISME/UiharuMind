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
