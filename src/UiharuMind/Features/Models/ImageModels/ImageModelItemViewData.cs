using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 生图模型列表的一行：只读快照，列表一变整体重建（条目少，不值得逐项通知）
/// </summary>
public sealed class ImageModelItemViewData
{
    /// <param name="model">生图模型</param>
    /// <param name="index">在回退顺序里的位置（从 0 起）</param>
    /// <param name="count">列表总数</param>
    public ImageModelItemViewData(ImageModelInfo model, int index, int count)
    {
        Model = model;
        Order = index + 1;
        CanMoveUp = index > 0;
        CanMoveDown = index < count - 1;

        ImageModelStatus status = ImageGenerationService.StatusOf(model);
        IsReady = status.State == EImageModelState.Ready;
        StatusText = status.State switch
        {
            EImageModelState.NotConfigured => Loc.Text(LangKey.ImageModelStateNotConfigured),
            EImageModelState.Cooling => Loc.Text(LangKey.ImageModelStateCooling, status.LastError ?? string.Empty),
            _ => Loc.Text(LangKey.ImageModelStateReady),
        };
    }

    /// <summary>对应的生图模型</summary>
    public ImageModelInfo Model { get; }

    /// <summary>回退顺序（从 1 起）</summary>
    public int Order { get; }

    /// <summary>名字</summary>
    public string Name => Model.Name;

    /// <summary>接口格式 · 模型 id · 分辨率档</summary>
    public string Detail => $"{Model.Dialect} · {Model.ModelId} · {Model.Resolution.ToTierLabel()}";

    /// <summary>服务地址</summary>
    public string Endpoint => Model.Endpoint;

    /// <summary>支持编辑</summary>
    public bool SupportsEditing => Model.SupportsEditing;

    /// <summary>可以接单</summary>
    public bool IsReady { get; }

    /// <summary>状态说明</summary>
    public string StatusText { get; }

    /// <summary>不是第一个</summary>
    public bool CanMoveUp { get; }

    /// <summary>不是最后一个</summary>
    public bool CanMoveDown { get; }
}
