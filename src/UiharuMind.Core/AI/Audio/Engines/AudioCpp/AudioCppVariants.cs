using System.Runtime.InteropServices;
using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// audio.cpp 发布包的变体，包名形如 audio-v0.9.1-bin-windows-x64-vulkan。
/// 推荐：macOS 用 Metal；Windows / Linux 用 CPU——audio.cpp 只把 CUDA 当优化路径，Vulkan 的模型覆盖不全，
/// 起步的小模型在 CPU 上就能实时。跑不起来不自动换，交给用户在引擎版本里另选
/// </summary>
public static class AudioCppVariants
{
    /// <summary>
    /// 包名（或由包名得到的版本名）是否本机的推荐变体
    /// </summary>
    /// <param name="packageName">包名</param>
    /// <returns>是推荐变体返回 true</returns>
    public static bool IsRecommended(string packageName)
    {
        OSPlatform? platform = EngineVariants.CurrentPlatform;
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
        string? arch = EngineVariants.ArchitectureToken(architecture);
        if (arch == null) return false;

        string marker = platform == OSPlatform.OSX ? $"-bin-macos-{arch}-metal"
            : platform == OSPlatform.Windows ? $"-bin-windows-{arch}-cpu"
            : platform == OSPlatform.Linux ? $"-bin-ubuntu-{arch}-cpu"
            : "";
        // 以变体结尾：cpu 不能把 cpu-portable 也认进来
        return marker.Length > 0 && packageName.EndsWith(marker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 包对应的 server 后端（server.json 的 backend）
    /// </summary>
    /// <param name="packageName">包名</param>
    /// <returns>metal / vulkan / cuda / cpu</returns>
    public static string GetBackend(string packageName)
    {
        if (packageName.Contains("-metal", StringComparison.OrdinalIgnoreCase)) return "metal";
        if (packageName.Contains("-vulkan", StringComparison.OrdinalIgnoreCase)) return "vulkan";
        if (packageName.Contains("-cuda", StringComparison.OrdinalIgnoreCase)) return "cuda";
        return "cpu";
    }
}
