using System.Runtime.InteropServices;
using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 一份 stable-diffusion.cpp 引擎包（发布号 × 平台变体）
/// </summary>
public class SdCppVersion : EnginePackage
{
    /// <summary>
    /// 是否本机的推荐变体
    /// </summary>
    public override bool IsRecommended => SdCppVariants.IsRecommended(Name);
}

/// <summary>
/// stable-diffusion.cpp 发布包的变体，包名形如 sd-master-&lt;sha&gt;-bin-win-vulkan-x64、
/// -bin-Darwin-macOS-26.6.2-arm64、-bin-Linux-Ubuntu-24.04-x86_64-vulkan。
/// 推荐：macOS 只有一种（双架构，内嵌 Metal）；Windows / Linux 用 Vulkan。
/// CUDA 包要另配 563MB 的运行时包、ROCm 只认部分显卡，都先不列；跑不起来不自动换，交给用户
/// </summary>
public static class SdCppVariants
{
    /// <summary>
    /// 包名（或由包名得到的版本名）是否本机的推荐变体
    /// </summary>
    /// <param name="packageName">包名</param>
    /// <returns>是推荐变体返回 true</returns>
    public static bool IsRecommended(string packageName) => EngineVariants.IsRecommended(packageName, Marker);

    /// <summary>
    /// 指定平台下包名是否推荐变体
    /// </summary>
    /// <param name="packageName">包名</param>
    /// <param name="platform">操作系统</param>
    /// <param name="architecture">CPU 架构</param>
    /// <returns>是推荐变体返回 true</returns>
    public static bool IsRecommended(string packageName, OSPlatform platform, Architecture architecture) =>
        EngineVariants.IsRecommended(packageName, platform, architecture, Marker);

    // macOS 包名里的架构是打包机器的，包本身是双架构
    private static string? Marker(OSPlatform platform, string arch) =>
        platform == OSPlatform.OSX ? "-bin-Darwin-macOS-"
        : platform == OSPlatform.Windows ? $"-bin-win-vulkan-{arch}"
        : platform == OSPlatform.Linux && arch == "x64" ? "-x86_64-vulkan"
        : null;
}
