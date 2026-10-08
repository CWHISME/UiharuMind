using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 语音模型家族详情里的一行包（一个语种 / 精度）
/// </summary>
/// <param name="package">包</param>
/// <param name="familyName">家族显示名（用来缩短包名）</param>
public partial class AudioPackageRowData(AudioCppPackage package, string familyName) : ObservableObject
{
    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _actionText = "";
    [ObservableProperty] private bool _canDownload;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private int _percent;
    [ObservableProperty] private string _riskTag = "Idle";
    [ObservableProperty] private string _riskTip = "";
    [ObservableProperty] private bool _isRecommended;
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
    public string Label { get; } = AudioModelText.PackageLabel(package, familyName);

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

    /// <summary>
    /// 套用下载状态
    /// </summary>
    /// <param name="status">状态</param>
    public void ApplyStatus(QuantDownloadStatus status)
    {
        StatusText = status.StatusText;
        ActionText = status.ActionText;
        CanDownload = status.CanDownload;
        IsDownloaded = status.State == EQuantDownloadState.Downloaded;
        ShowProgress = status.ShowsProgress;
        Percent = status.Percent;
    }

    /// <summary>
    /// 按下载前估算刷新红黄绿点
    /// </summary>
    /// <param name="level">风险档位</param>
    /// <param name="estimatedBytes">估算占用</param>
    /// <param name="device">设备信息</param>
    public void ApplyRisk(RuntimeLoadRiskLevel level, long estimatedBytes, RuntimeDeviceInfo device)
    {
        RiskTag = RuntimeRiskBadge.Tag(level);
        RiskTip = RuntimeRiskBadge.Tip(estimatedBytes, device);
    }

    partial void OnIsDownloadedChanged(bool value) => OnPropertyChanged(nameof(CanSetDefault));

    partial void OnIsDefaultChanged(bool value) => OnPropertyChanged(nameof(CanSetDefault));
}
