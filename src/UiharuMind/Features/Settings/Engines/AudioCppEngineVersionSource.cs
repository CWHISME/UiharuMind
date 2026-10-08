using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// audio.cpp：走 <see cref="AudioCppEngine"/> 与它的安装器
/// </summary>
public sealed partial class AudioCppEngineVersionSource : IEngineVersionSource
{
    private readonly AudioCppEngine _engine;

    public AudioCppEngineVersionSource(AudioCppEngine engine)
    {
        _engine = engine;
        engine.Installer.Installed += _ => Installed?.Invoke();
        engine.Installer.AvailableUpdateChanged += () => AvailableUpdateChanged?.Invoke();
    }

    public event Action? Installed;
    public event Action? AvailableUpdateChanged;

    public string EngineName => "audio.cpp";
    public string IconName => "audio-lines";
    public string VersionDescription => Loc.Text(LangKey.AudioEngineVersionDesc);
    public string RootDirectory => _engine.EngineRoot;
    public EnginePackage? CurrentVersion => _engine.CurrentVersion;
    public EnginePackage? AvailableUpdate => _engine.Installer.AvailableUpdate;
    public bool HasChannels => false;
    public bool UseStableChannel { get; set; } = true;

    public async Task<IReadOnlyList<EnginePackage>> GetLocalVersionsAsync()
    {
        return (await _engine.GetLocalVersionsAsync()).ToList<EnginePackage>();
    }

    public async Task<EngineRemoteVersions> PullRemoteVersionsAsync()
    {
        IReadOnlyList<AudioCppVersion> versions = await _engine.PullLatestVersionsAsync();
        _engine.Installer.ApplyVersions(versions);
        return new EngineRemoteVersions(versions.ToList<EnginePackage>(), _engine.ReleaseInfoText);
    }

    public void Select(EnginePackage version) => _engine.SelectVersion((AudioCppVersion)version);

    public Task InstallAsync(EnginePackage version) => _engine.Installer.InstallAsync((AudioCppVersion)version);

    public DownloadJob Enqueue(EnginePackage version) => _engine.Installer.Enqueue((AudioCppVersion)version, true);

    /// <summary>
    /// 从版本名里认出发布号（audio-v0.9.1-bin-… 里的 v0.9.1），拼 GitHub 发布页
    /// </summary>
    public string? ReleaseUrl(EnginePackage version)
    {
        Match match = TagPattern().Match(version.Name);
        return match.Success ? $"https://github.com/0xShug0/audio.cpp/releases/tag/{match.Groups[1].Value}" : null;
    }

    [GeneratedRegex(@"^audio-(.+?)-bin-")]
    private static partial Regex TagPattern();
}
