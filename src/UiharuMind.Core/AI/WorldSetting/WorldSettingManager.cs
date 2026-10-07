using System.Linq;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.WorldSettings;

/// <summary>
/// 世界书容器（共享世界设定，镜像知识库 MemoryManager 的形态）。
/// 书是命名实体、落 Data/WorldBooks/*.json；角色按名挂载（<see cref="CharacterData.WorldSettingName"/>）。
/// </summary>
public class WorldSettingManager : UniquieContainerSingleton<WorldSettingManager, WorldSetting>
{
    protected override string SaveRootPath => AppPaths.Data.WorldBooks;

    protected override void OnOrderedItems(List<WorldSetting> items)
    {
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    /// <summary>取一本书；空名或不存在返回 null</summary>
    public WorldSetting? Get(string name) =>
        string.IsNullOrEmpty(name) ? null : ItemDictionary.GetValueOrDefault(name);

    /// <summary>
    /// 把条目并入指定书：书不存在则新建（名字原样，不加唯一后缀）；已存在则按 (Keys+Content) 去重后追加。
    /// 角色卡 character_book 导入与旧数据迁移共用这一处。
    /// </summary>
    public WorldSetting Merge(string name, IEnumerable<WorldSettingEntry> entries)
    {
        WorldSetting book = Get(name);
        if (book == null)
        {
            book = AddNewItem(name);
        }

        foreach (WorldSettingEntry entry in entries)
        {
            if (book.Entries.Any(e =>
                    e.Content == entry.Content && e.Keys.SequenceEqual(entry.Keys))) continue;
            book.Entries.Add(entry);
        }

        Save(book);
        return book;
    }
}