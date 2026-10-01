using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 工具结果里产出的一张图：缩略图常驻，原图点开时从盘上现读
/// </summary>
/// <param name="FilePath">图片绝对路径</param>
/// <param name="Thumbnail">缩略图</param>
public sealed record ToolResultImage(string FilePath, Bitmap Thumbnail);

/// <summary>
/// 生图工具的卡片直接显示产出的图（ADR 0052）：图不交回模型，模型忘了贴图用户也看得到；
/// 看图工具显示模型看到的那份预览（ADR 0053）。卡片收起时照样显示，看图不该要先展开一张工具卡。
/// </summary>
public partial class ToolCallItem
{
    /// <summary>缩略图解码宽度：只解这么大，卡片常驻不攥原图的几 MB（ADR 0041 的内存账）</summary>
    private const int ThumbnailWidth = 320;

    /// <summary>产出的图，按结果里的顺序</summary>
    public ObservableCollection<ToolResultImage> ResultImages { get; } = [];

    /// <summary>有图可显示</summary>
    public bool HasResultImages => ResultImages.Count > 0;

    /// <summary>
    /// 生图、看图成功后按结果里的路径载入缩略图。路径多是草稿目录简写，按会话的路径口径展开；
    /// 文件已被删掉的那张静默跳过
    /// </summary>
    /// <param name="paths">会话的路径口径；拿不到时只认绝对路径</param>
    public void LoadResultImages(AgentPathResolver? paths)
    {
        if (!IsSuccess || HasResultImages) return;
        IReadOnlyList<string> produced = ToolName switch
        {
            ImageGenerationTool.ToolName => ImageGenerationTool.ParseSavedPaths(ResultText),
            ViewImageTool.ToolName => ViewImageTool.ParsePreviewPaths(ResultText),
            _ => [],
        };
        if (produced.Count == 0) return;

        foreach (string path in produced)
        {
            string full = paths != null && paths.TryResolve(path, out string resolved) ? resolved : path;
            if (TryLoadThumbnail(full) is { } image) ResultImages.Add(image);
        }

        OnPropertyChanged(nameof(HasResultImages));
    }

    /// <inheritdoc />
    public override void ReleaseImages()
    {
        // 先摘绑定再释放，顺序反了就是把还在界面上的位图放掉
        ToolResultImage[] stale = ResultImages.ToArray();
        ResultImages.Clear();
        foreach (ToolResultImage image in stale) image.Thumbnail.Dispose();
        OnPropertyChanged(nameof(HasResultImages));
    }

    [RelayCommand]
    private static void OpenResultImage(ToolResultImage image) => FileOpener.OpenPreviewImage(image.FilePath);

    private static ToolResultImage? TryLoadThumbnail(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            return new ToolResultImage(path, Bitmap.DecodeToWidth(stream, ThumbnailWidth));
        }
        catch (Exception e)
        {
            Log.Warning($"Load generated image failed '{path}': {e.Message}");
            return null;
        }
    }
}
