using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 仓库详情里的一行量化
/// </summary>
public partial class ModelQuantRowData(ModelRepoQuant quant, string mainFilePath, string? conflictPath) : ObservableObject
{
    [ObservableProperty] private string _riskTag = "Idle";
    [ObservableProperty] private string _riskTip = "";
    [ObservableProperty] private bool _isRecommended;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _actionText = "";
    [ObservableProperty] private bool _canDownload;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private int _percent;

    /// <summary>
    /// 量化
    /// </summary>
    public ModelRepoQuant Quant { get; } = quant;

    /// <summary>
    /// 主文件（分片为首片）的落盘路径
    /// </summary>
    public string MainFilePath { get; } = mainFilePath;

    /// <summary>
    /// 本地同名模型的路径；没有冲突为 null
    /// </summary>
    public string? ConflictPath { get; } = conflictPath;

    /// <summary>
    /// 量化标签；认不出时用文件名
    /// </summary>
    public string Label => Quant.Quantization.Length > 0 ? Quant.Quantization : Quant.Name;

    /// <summary>
    /// 体积
    /// </summary>
    public string SizeText => GameUtils.FormatBytes(Quant.TotalSize);

    /// <summary>
    /// 分片数提示，单文件为空
    /// </summary>
    public string PartsText => Quant.Files.Count > 1 ? Loc.Text(LangKey.ModelDownloadParts, Quant.Files.Count) : "";

    /// <summary>
    /// 本地已有同名模型时的悬停说明
    /// </summary>
    public string? ConflictTip => ConflictPath == null ? null : Loc.Text(LangKey.ModelDownloadNameConflictTip, ConflictPath);

    /// <summary>
    /// 当前状态
    /// </summary>
    public QuantDownloadStatus Status { get; private set; }

    /// <summary>
    /// 按下载前估算刷新红黄绿点
    /// </summary>
    /// <param name="level">风险档位</param>
    /// <param name="estimatedBytes">估算占用</param>
    /// <param name="device">设备信息</param>
    public void ApplyRisk(RuntimeLoadRiskLevel level, long estimatedBytes, RuntimeDeviceInfo device)
    {
        RiskTag = level switch
        {
            RuntimeLoadRiskLevel.Low => "Ready",
            RuntimeLoadRiskLevel.Warning => "Warning",
            RuntimeLoadRiskLevel.Danger => "Error",
            _ => "Idle"
        };
        RiskTip = device.HasMemoryInfo
            ? Loc.Text(LangKey.ModelDownloadRiskTip, GameUtils.FormatBytes(estimatedBytes),
                GameUtils.FormatBytes(device.TotalMemoryBytes))
            : Loc.Text(LangKey.ModelRuntimeRiskUnknown);
    }

    /// <summary>
    /// 刷新状态文案与按钮
    /// </summary>
    /// <param name="status">状态</param>
    public void ApplyStatus(QuantDownloadStatus status)
    {
        Status = status;
        StatusText = status.State switch
        {
            EQuantDownloadState.Queued => Loc.Text(LangKey.ModelDownloadStateQueued),
            EQuantDownloadState.Downloading => Loc.Text(LangKey.ModelDownloadStateDownloading, status.Percent),
            EQuantDownloadState.Paused => Loc.Text(LangKey.ModelDownloadStatePausedPercent, status.Percent),
            EQuantDownloadState.Failed => Loc.Text(LangKey.ModelDownloadStateFailedShort),
            EQuantDownloadState.Partial => Loc.Text(LangKey.ModelDownloadStatePartial, status.Percent),
            EQuantDownloadState.Downloaded => Loc.Text(LangKey.ModelDownloadStateDownloaded),
            EQuantDownloadState.NameConflict => Loc.Text(LangKey.ModelDownloadStateNameConflict),
            _ => Loc.Text(LangKey.ModelDownloadStateNotDownloaded)
        };
        ActionText = status.State switch
        {
            EQuantDownloadState.Paused or EQuantDownloadState.Partial => Loc.Text(LangKey.ModelDownloadResume),
            EQuantDownloadState.Failed => Loc.Text(LangKey.ModelDownloadRetry),
            _ => Loc.Text(LangKey.ModelDownloadAction)
        };
        CanDownload = status.CanDownload;
        IsDownloaded = status.State == EQuantDownloadState.Downloaded;
        ShowProgress = status.State is EQuantDownloadState.Downloading or EQuantDownloadState.Paused
            or EQuantDownloadState.Partial or EQuantDownloadState.Queued;
        Percent = status.Percent;
    }
}
