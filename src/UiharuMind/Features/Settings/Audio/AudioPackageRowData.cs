using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// 获取语音模型里的一行包
/// </summary>
public partial class AudioPackageRowData(AudioCppPackage package) : ObservableObject
{
    private const int MaxLanguagesShown = 6;

    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _actionText = "";
    [ObservableProperty] private bool _canDownload;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private int _percent;

    /// <summary>
    /// 包
    /// </summary>
    public AudioCppPackage Package { get; } = package;

    /// <summary>
    /// 仓库里的文件（含大小）；还没列到为 null
    /// </summary>
    public IReadOnlyList<ModelRepoFile>? RemoteFiles { get; private set; }

    public string Title => Package.DisplayName.Length > 0 ? Package.DisplayName : Package.Id;
    public bool IsRecommended => AudioCppModelCatalog.Recommended.Contains(Package.Id);
    public bool NeedsEspeak => Package.NeedsEspeak;

    /// <summary>
    /// 合成 / 识别
    /// </summary>
    public string TaskText => Loc.Text(Package.Task == EAudioCppTask.Tts ? LangKey.AudioTaskTts : LangKey.AudioTaskAsr);

    /// <summary>
    /// 语种（过多时截断）
    /// </summary>
    public string LanguagesText => Package.Languages.Count <= MaxLanguagesShown
        ? string.Join(", ", Package.Languages)
        : string.Join(", ", Package.Languages.Take(MaxLanguagesShown)) + " …";

    /// <summary>
    /// 上游成熟度：supported 不标，认得的翻成中文，其余原样标出来
    /// </summary>
    public string? MaturityText => Package.Status switch
    {
        "" or "supported" => null,
        "experimental" => Loc.Text(LangKey.AudioMaturityExperimental),
        "community" => Loc.Text(LangKey.AudioMaturityCommunity),
        "testing" => Loc.Text(LangKey.AudioMaturityTesting),
        "wip" => Loc.Text(LangKey.AudioMaturityWip),
        _ => Package.Status
    };

    /// <summary>
    /// 记下仓库里的文件与总大小
    /// </summary>
    public void ApplyRemoteFiles(IReadOnlyList<ModelRepoFile> files)
    {
        RemoteFiles = files;
        SizeText = GameUtils.FormatBytes(files.Sum(x => x.Size));
    }

    /// <summary>
    /// 套用下载状态
    /// </summary>
    public void ApplyStatus(QuantDownloadStatus status)
    {
        StatusText = status.StatusText;
        ActionText = status.ActionText;
        CanDownload = status.CanDownload;
        IsDownloaded = status.State == EQuantDownloadState.Downloaded;
        ShowProgress = status.ShowsProgress;
        Percent = status.Percent;
    }
}
