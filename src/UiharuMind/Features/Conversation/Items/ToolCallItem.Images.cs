using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 生图工具的卡片直接显示产出的图（ADR 0052）：图不交回模型，模型忘了贴图用户也看得到；
/// 看图工具显示模型看到的那份预览（ADR 0053）。卡片收起时照样显示，看图不该要先展开一张工具卡。
/// </summary>
public partial class ToolCallItem
{
    /// <summary>缩略图解码宽度：只解这么大，卡片常驻不攥原图的几 MB（ADR 0041 的内存账）</summary>
    private const int ThumbnailWidth = 320;

    private readonly ThumbnailStrip _resultImages = new();

    /// <summary>产出的图，按结果里的顺序</summary>
    public ObservableCollection<ImageThumbnail> ResultImages => _resultImages.Items;

    /// <summary>有图可显示</summary>
    public bool HasResultImages => ResultImages.Count > 0;

    /// <summary>
    /// 生图、看图成功后按结果里的路径载入缩略图，不等解码（见 <see cref="LoadResultImagesAsync"/>）
    /// </summary>
    /// <param name="paths">会话的路径口径；拿不到时只认绝对路径</param>
    public void LoadResultImages(AgentPathResolver? paths) => _ = LoadResultImagesAsync(paths);

    /// <summary>
    /// 按结果里的路径载入缩略图。路径多是草稿目录简写，按会话的路径口径展开；文件已被删掉的那张静默跳过。
    /// 解码放到线程池：切回一个图多的会话时，逐张同步解码会卡住界面线程。须在界面线程调用
    /// </summary>
    /// <param name="paths">会话的路径口径；拿不到时只认绝对路径</param>
    /// <returns>解完并挂上（或因条目已被释放而丢弃）时完成</returns>
    public async Task LoadResultImagesAsync(AgentPathResolver? paths)
    {
        if (!IsSuccess || HasResultImages) return;
        IReadOnlyList<string> produced = ToolName switch
        {
            ImageGenerationTool.ToolName => ImageGenerationTool.ParseSavedPaths(ResultText),
            ViewImageTool.ToolName => ViewImageTool.ParsePreviewPaths(ResultText),
            _ => [],
        };
        if (produced.Count == 0) return;

        List<ImageThumbnailSource> sources = produced
            .Select(path => ImageThumbnailSource.FromFile(
                paths != null && paths.TryResolve(path, out string resolved) ? resolved : path))
            .ToList();
        await _resultImages.LoadAsync(sources, ThumbnailWidth);
        OnPropertyChanged(nameof(HasResultImages));
    }

    /// <inheritdoc />
    public override void ReleaseImages()
    {
        _resultImages.Release();
        OnPropertyChanged(nameof(HasResultImages));
    }

    [RelayCommand]
    private static void OpenResultImage(ImageThumbnail image)
    {
        if (image.FilePath != null) FileOpener.OpenPreviewImage(image.FilePath);
    }
}
