using System.Linq;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Features.Models.ImageModels;

namespace UiharuMind.Features.Models.Downloads.Image;

/// <summary>
/// 家族详情里的一档：所有组件一起下，大小是它们之和
/// </summary>
public sealed class ImageTierRowData : DownloadRowDataBase
{
    /// <param name="tier">档位</param>
    public ImageTierRowData(ImageCatalogTier tier)
    {
        Tier = tier;
        IsRecommended = tier.Recommended;
        SizeText = GameUtils.FormatBytes(tier.TotalSize);
    }

    /// <summary>
    /// 档位
    /// </summary>
    public ImageCatalogTier Tier { get; }

    public override string Label => ImageTierText.Label(Tier.Id);

    /// <summary>
    /// 悬停看这一档由哪些文件组成
    /// </summary>
    public override string LabelTip => string.Join('\n',
        Tier.Components.Select(x => $"{x.Flag}: {x.Repo}/{x.File} ({GameUtils.FormatBytes(x.Size)})"));
}
