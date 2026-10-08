using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 语音模型家族详情里的一行包（一个语种 / 精度）
/// </summary>
/// <param name="package">包</param>
/// <param name="familyName">家族显示名（用来缩短包名）</param>
public partial class AudioPackageRowData(AudioCppPackage package, string familyName) : DownloadRowDataBase
{
    [ObservableProperty] private bool _isDefault;

    /// <summary>
    /// 包
    /// </summary>
    public AudioCppPackage Package { get; } = package;

    /// <summary>
    /// 仓库里的文件（含大小）；还没列到为 null
    /// </summary>
    public IReadOnlyList<ModelRepoFile>? RemoteFiles { get; private set; }

    /// <summary>
    /// 短名，如 English Q8_0
    /// </summary>
    public override string Label { get; } = AudioModelText.PackageLabel(package, familyName);

    public override string? LabelTip => Package.DisplayName;

    /// <summary>
    /// 已下载且不是默认：可以设为默认
    /// </summary>
    public bool CanSetDefault => IsDownloaded && !IsDefault;

    /// <summary>
    /// 记下仓库里的文件与总大小
    /// </summary>
    /// <param name="files">包里的远端文件</param>
    public void ApplyRemoteFiles(IReadOnlyList<ModelRepoFile> files)
    {
        RemoteFiles = files;
        SizeText = GameUtils.FormatBytes(files.Sum(x => x.Size));
    }

    protected override void OnDownloadedChanged() => OnPropertyChanged(nameof(CanSetDefault));

    partial void OnIsDefaultChanged(bool value) => OnPropertyChanged(nameof(CanSetDefault));
}
