using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// llama.cpp：走 <see cref="LlmManager"/> 与 <see cref="LLamaCppEngineInstaller.Shared"/>
/// </summary>
public sealed partial class LLamaCppEngineVersionSource : IEngineVersionSource
{
    public LLamaCppEngineVersionSource()
    {
        LLamaCppEngineInstaller.Shared.Installed += _ => Installed?.Invoke();
        LLamaCppEngineInstaller.Shared.AvailableUpdateChanged += () => AvailableUpdateChanged?.Invoke();
    }

    public event Action? Installed;
    public event Action? AvailableUpdateChanged;

    public string EngineName => "llama.cpp";
    public string IconName => "terminal";
    public string VersionDescription => Loc.Text(LangKey.LLamaCppVersionDesc);
    public string RootDirectory => Path.Combine(AppPaths.External.Engine, "LLamaCpp");
    public EnginePackage? CurrentVersion => LlmManager.Instance.CurrentRuntimeVersion;
    public EnginePackage? AvailableUpdate => LLamaCppEngineInstaller.Shared.AvailableUpdate;
    public bool HasChannels => true;

    public bool UseStableChannel
    {
        get => LLamaCppSettingConfig.Current.UseStableChannel;
        set
        {
            LLamaCppSettingConfig.Current.UseStableChannel = value;
            LLamaCppSettingConfig.Current.Save();
        }
    }

    public async Task<IReadOnlyList<EnginePackage>> GetLocalVersionsAsync()
    {
        return (await LlmManager.Instance.GetLocalRuntimeVersions()).VersionsList.ToList<EnginePackage>();
    }

    public async Task<EngineRemoteVersions> PullRemoteVersionsAsync()
    {
        VersionManager versions = await LlmManager.Instance.PullLatestRuntimeVersion();
        LLamaCppEngineInstaller.Shared.ApplyVersions(versions.VersionsList);
        return new EngineRemoteVersions(versions.VersionsList.ToList<EnginePackage>(), versions.ReleaseDate);
    }

    public void Select(EnginePackage version) => LlmManager.Instance.SetSelectedRuntimeVersion((VersionInfo)version);

    public Task InstallAsync(EnginePackage version) => LLamaCppEngineInstaller.Shared.InstallAsync((VersionInfo)version);

    public DownloadJob Enqueue(EnginePackage version) => LLamaCppEngineInstaller.Shared.Enqueue((VersionInfo)version, true);

    /// <summary>
    /// 从版本名里认出构建号（b1234），拼 GitHub 发布页
    /// </summary>
    /// <param name="version">版本，名字如 llama-b11443-bin-macos-arm64</param>
    /// <returns>发布页地址；认不出为 null</returns>
    public string? ReleaseUrl(EnginePackage version) => ReleaseUrl(version.Name);

    /// <summary>
    /// 从版本名里认出构建号，拼 GitHub 发布页
    /// </summary>
    /// <param name="versionName">版本名</param>
    /// <returns>发布页地址；认不出为 null</returns>
    public static string? ReleaseUrl(string versionName)
    {
        Match match = BuildTagPattern().Match(versionName);
        return match.Success ? $"https://github.com/ggml-org/llama.cpp/releases/tag/{match.Value}" : null;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])b\d+(?![A-Za-z0-9])")]
    private static partial Regex BuildTagPattern();
}
