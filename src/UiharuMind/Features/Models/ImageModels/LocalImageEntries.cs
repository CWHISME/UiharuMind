using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.ImageGeneration;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 从可下载列表的档位建本地生图条目：下载装好时建一个，打开页签时把「组件都在、条目没了」的补回来
/// </summary>
public static class LocalImageEntries
{
    /// <summary>
    /// 建一个条目。名字取家族名，重名再带上档位
    /// </summary>
    /// <param name="family">家族</param>
    /// <param name="tier">档位</param>
    /// <param name="takenNames">已用的名字</param>
    /// <returns>条目</returns>
    public static ImageModelInfo Create(ImageCatalogFamily family, ImageCatalogTier tier, IEnumerable<string> takenNames)
    {
        HashSet<string> taken = takenNames.ToHashSet(StringComparer.Ordinal);
        string name = taken.Contains(family.Name) ? $"{family.Name} ({ImageTierText.Label(tier.Id)})" : family.Name;
        for (int i = 2; taken.Contains(name); i++) name = $"{family.Name} ({ImageTierText.Label(tier.Id)} {i})";
        return LocalImageModels.Create(family, tier, name);
    }

    /// <summary>
    /// 组件已在本机、列表里却没有条目的档位（别的档案下载的、配置丢了的），各建一个条目
    /// </summary>
    /// <param name="catalog">可下载列表</param>
    /// <param name="existing">现有条目</param>
    /// <param name="isInstalled">这一档的组件都在本机</param>
    /// <returns>要补的条目；没有为空</returns>
    public static IReadOnlyList<ImageModelInfo> FindUnlisted(ImageModelCatalog catalog,
        IReadOnlyList<ImageModelInfo> existing, Func<ImageCatalogTier, bool> isInstalled)
    {
        HashSet<(string?, string?)> listed = existing.Where(x => x.Local != null)
            .Select(x => (x.Local!.Family, x.Local.Tier)).ToHashSet();
        List<string> names = existing.Select(x => x.Name).ToList();
        List<ImageModelInfo> found = [];
        foreach (ImageCatalogFamily family in catalog.Families)
        foreach (ImageCatalogTier tier in family.Tiers)
        {
            if (listed.Contains((family.Id, tier.Id)) || !isInstalled(tier)) continue;
            ImageModelInfo entry = Create(family, tier, names);
            names.Add(entry.Name);
            found.Add(entry);
        }
        return found;
    }
}
