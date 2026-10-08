using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 「获取模型」详情里的一行可下载项：风险点、推荐、大小、下载状态与进度。
/// 行的外观由 <see cref="DownloadRowView"/> 统一画，右侧按钮与名字旁的徽标由各分类自己给
/// </summary>
public abstract partial class DownloadRowDataBase : ObservableObject
{
    [ObservableProperty] private string _riskTag = "Idle";
    [ObservableProperty] private string _riskTip = "";
    [ObservableProperty] private bool _isRecommended;
    [ObservableProperty] private string _sizeText = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _actionText = "";
    [ObservableProperty] private bool _canDownload;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private int _percent;

    /// <summary>
    /// 行名
    /// </summary>
    public abstract string Label { get; }

    /// <summary>
    /// 行名的悬停说明；没有为 null
    /// </summary>
    public virtual string? LabelTip => null;

    /// <summary>
    /// 状态行的悬停说明；没有为 null
    /// </summary>
    public virtual string? StatusTip => null;

    /// <summary>
    /// 当前下载状态
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
        RiskTag = RuntimeRiskBadge.Tag(level);
        RiskTip = RuntimeRiskBadge.Tip(estimatedBytes, device);
    }

    /// <summary>
    /// 套用下载状态
    /// </summary>
    /// <param name="status">状态</param>
    public void ApplyStatus(QuantDownloadStatus status)
    {
        Status = status;
        StatusText = status.StatusText;
        ActionText = status.ActionText;
        CanDownload = status.CanDownload;
        IsDownloaded = status.State == EQuantDownloadState.Downloaded;
        ShowProgress = status.ShowsProgress;
        Percent = status.Percent;
    }

    /// <summary>
    /// 下没下好变了
    /// </summary>
    protected virtual void OnDownloadedChanged()
    {
    }

    partial void OnIsDownloadedChanged(bool value) => OnDownloadedChanged();
}
