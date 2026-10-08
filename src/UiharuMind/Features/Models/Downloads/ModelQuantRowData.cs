using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 仓库详情里的一行量化
/// </summary>
public partial class ModelQuantRowData : DownloadRowDataBase
{
    [ObservableProperty] private bool _isSelected;

    /// <param name="quant">量化</param>
    /// <param name="mainFilePath">主文件（分片为首片）的落盘路径</param>
    /// <param name="conflictPath">本地同名模型的路径；没有冲突为 null</param>
    public ModelQuantRowData(ModelRepoQuant quant, string mainFilePath, string? conflictPath)
    {
        Quant = quant;
        MainFilePath = mainFilePath;
        ConflictPath = conflictPath;
        SizeText = GameUtils.FormatBytes(quant.TotalSize);
    }

    /// <summary>
    /// 量化
    /// </summary>
    public ModelRepoQuant Quant { get; }

    /// <summary>
    /// 主文件（分片为首片）的落盘路径
    /// </summary>
    public string MainFilePath { get; }

    /// <summary>
    /// 本地同名模型的路径；没有冲突为 null
    /// </summary>
    public string? ConflictPath { get; }

    /// <summary>
    /// 量化标签；认不出时用文件名
    /// </summary>
    public override string Label => Quant.Quantization.Length > 0 ? Quant.Quantization : Quant.Name;

    /// <summary>
    /// 分片数提示，单文件为空
    /// </summary>
    public string PartsText => Quant.Files.Count > 1 ? Loc.Text(LangKey.ModelDownloadParts, Quant.Files.Count) : "";

    /// <summary>
    /// 本地已有同名模型时的悬停说明
    /// </summary>
    public string? ConflictTip => ConflictPath == null ? null : Loc.Text(LangKey.ModelDownloadNameConflictTip, ConflictPath);

    public override string? StatusTip => ConflictTip;
}
