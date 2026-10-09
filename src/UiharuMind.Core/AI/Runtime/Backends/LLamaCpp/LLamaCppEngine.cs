using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// llama.cpp 文本引擎的分发（版本、安装、更新通道）。进程按模型起，归 <see cref="LLamaCppRuntimeService"/> 与嵌入会话
/// </summary>
public static partial class LLamaCppEngine
{
    private static readonly Lazy<EngineDistribution<VersionInfo>> DistributionInstance =
        new(() => Create(LLamaCppSettingConfig.Current, DownloadQueue.Shared, EngineRoot));

    /// <summary>
    /// 下载的 llama.cpp 引擎包放这里
    /// </summary>
    public static string EngineRoot => Path.Combine(AppPaths.External.Engine, "LLamaCpp");

    /// <summary>
    /// 走应用档案与全局下载队列的那一份（与另两种引擎的 <c>Shared.Distribution</c> 对应）
    /// </summary>
    public static EngineDistribution<VersionInfo> Distribution => DistributionInstance.Value;

    /// <summary>
    /// 建一份分发
    /// </summary>
    /// <param name="settings">引擎设置（选中的版本、更新通道、随包引擎目录）</param>
    /// <param name="queue">下载队列</param>
    /// <param name="engineRoot">引擎包目录</param>
    /// <returns>分发</returns>
    public static EngineDistribution<VersionInfo> Create(LLamaCppSettingConfig settings, DownloadQueue queue,
        string engineRoot)
    {
        LLamaCppVersionManager versions = new(() => settings.UseStableChannel,
            Path.Combine(settings.DefaultRuntimePath, "LLamaCpp"));
        return new EngineDistribution<VersionInfo>("llama.cpp", engineRoot, versions, queue, settings, ReleaseUrl,
            new Channels(settings));
    }

    /// <summary>
    /// 从版本名里认出构建号（b1234），拼 GitHub 发布页
    /// </summary>
    /// <param name="versionName">版本名，如 llama-b11443-bin-macos-arm64</param>
    /// <returns>发布页地址；认不出为 null</returns>
    public static string? ReleaseUrl(string versionName)
    {
        Match match = BuildTagPattern().Match(versionName);
        return match.Success ? $"https://github.com/ggml-org/llama.cpp/releases/tag/{match.Value}" : null;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])b\d+(?![A-Za-z0-9])")]
    private static partial Regex BuildTagPattern();

    // 预览版每天都有新构建，正式版只跟正式版钉住的构建
    private sealed class Channels(LLamaCppSettingConfig settings) : IEngineUpdateChannels
    {
        public bool UseStableChannel
        {
            get => settings.UseStableChannel;
            set
            {
                settings.UseStableChannel = value;
                settings.Save();
            }
        }
    }
}
