using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// audio.cpp（0xShug0/audio.cpp）的版本管理，以 audiocpp_server 存在为安装成功的判据
/// </summary>
public partial class AudioCppVersionManager : EngineVersionManager<AudioCppVersion>
{
    /// <summary>
    /// 服务端可执行文件名（不含扩展名）
    /// </summary>
    public const string ExecutableName = "audiocpp_server";

    public override string ServerExeName => ExecutableName;

    protected override string Owner => "0xShug0";
    protected override string Repository => "audio.cpp";

    protected override GitHubReleaseAssetSelectOptions GetAssetSelectOptions()
    {
        return new GitHubReleaseAssetSelectOptions(NamePrefix: "audio-");
    }

    // 版本名里的发布号（audio-v0.9.1-bin-… 里的 v0.9.1）
    protected override string? TagOf(string versionName)
    {
        Match match = TagPattern().Match(versionName);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^audio-(.+?)-bin-")]
    private static partial Regex TagPattern();

    // CUDA 包要另配 cudart 包才能跑，colab 包是给云端 T4 的；都先不列
    protected override bool ShouldIncludeAsset(GitHubReleaseAssetInfo asset)
    {
        return !asset.Name.Contains("-cuda", StringComparison.OrdinalIgnoreCase)
               && !asset.Name.Contains("-cudart-", StringComparison.OrdinalIgnoreCase);
    }
}
