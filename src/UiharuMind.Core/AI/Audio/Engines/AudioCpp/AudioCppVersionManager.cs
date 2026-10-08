using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// audio.cpp（0xShug0/audio.cpp）的版本管理：拉 GitHub releases、筛本平台包、解压到引擎目录，
/// 以 audiocpp_server 存在为安装成功的判据
/// </summary>
public class AudioCppVersionManager : ReleaseVersionManagerBase<AudioCppVersion>
{
    /// <summary>
    /// 服务端可执行文件名（不含扩展名）
    /// </summary>
    public const string ServerExeName = "audiocpp_server";

    protected override string Owner => "0xShug0";
    protected override string Repository => "audio.cpp";

    protected override GitHubReleaseAssetSelectOptions GetAssetSelectOptions()
    {
        return new GitHubReleaseAssetSelectOptions(NamePrefix: "audio-");
    }

    // CUDA 包要另配 cudart 包才能跑，colab 包是给云端 T4 的；都先不列
    protected override bool ShouldIncludeAsset(GitHubReleaseAssetInfo asset)
    {
        return !asset.Name.Contains("-cuda", StringComparison.OrdinalIgnoreCase)
               && !asset.Name.Contains("-cudart-", StringComparison.OrdinalIgnoreCase);
    }

    protected override void ConfigureLocalVersion(AudioCppVersion version, string versionDirectory)
    {
        version.ExecutablePath = versionDirectory;
    }

    protected override ManagedVersionValidationResult ValidateInstalledVersion(AudioCppVersion version)
    {
        if (!Directory.Exists(version.InstallDirectory))
            return new ManagedVersionValidationResult(false, "Runtime directory does not exist.");

        foreach (string file in Directory.EnumerateFiles(
                     version.InstallDirectory, ServerExeName + "*", SearchOption.AllDirectories))
        {
            version.ExecutablePath = Path.GetDirectoryName(file)!;
            return ManagedVersionValidationResult.Valid;
        }

        return new ManagedVersionValidationResult(false, $"{ServerExeName} was not found.");
    }

    protected override string CreateReleaseInfoText(GitHubReleaseInfo release)
    {
        string? releaseDate = release.PublishedAt.HasValue
            ? TimeUtils.TimeStringToLocalTimeString(release.PublishedAt.Value.ToString("O"))
            : null;
        string body = MarkdownCleaner.Clean(release.Body ?? "");
        string title = releaseDate == null ? $"**{release.TagName}**" : $"**{release.TagName}** · {releaseDate}";
        return $"{title}\n\n{body}".TrimEnd();
    }
}
