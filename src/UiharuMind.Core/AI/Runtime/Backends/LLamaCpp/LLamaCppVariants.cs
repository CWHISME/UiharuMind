using System.Runtime.InteropServices;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// llama.cpp 发布包的推荐变体：macOS 只有一种（含 Metal），Windows / Linux 用 Vulkan（N 卡、A 卡、核显都能用）。
/// 跑不起来不自动换，交给用户在引擎版本里另选
/// </summary>
public static class LLamaCppVariants
{
    /// <summary>
    /// 包名（或由包名得到的版本名）是否本机的推荐变体
    /// </summary>
    /// <param name="packageName">如 llama-b11438-bin-win-vulkan-x64</param>
    /// <returns>是推荐变体返回 true</returns>
    public static bool IsRecommended(string packageName)
    {
        OSPlatform? platform = OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsLinux() ? OSPlatform.Linux
            : null;
        return platform != null && IsRecommended(packageName, platform.Value, RuntimeInformation.ProcessArchitecture);
    }

    /// <summary>
    /// 指定平台下包名是否推荐变体
    /// </summary>
    /// <param name="packageName">包名</param>
    /// <param name="platform">操作系统</param>
    /// <param name="architecture">CPU 架构</param>
    /// <returns>是推荐变体返回 true</returns>
    public static bool IsRecommended(string packageName, OSPlatform platform, Architecture architecture)
    {
        string? arch = architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => null
        };
        if (arch == null) return false;

        string marker = platform == OSPlatform.OSX ? $"-bin-macos-{arch}"
            : platform == OSPlatform.Windows ? $"-bin-win-vulkan-{arch}"
            : platform == OSPlatform.Linux ? $"-bin-ubuntu-vulkan-{arch}"
            : "";
        return marker.Length > 0 && packageName.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
