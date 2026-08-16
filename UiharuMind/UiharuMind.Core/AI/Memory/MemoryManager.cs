using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 知识库容器。
///
/// 相比别的容器子类，<see cref="MemoryData"/> 多带一份 side-car（索引库 .sqlite），
/// 而基类只认 json。所以删除与改名都在这里收口——否则契约就变成「调用方记得多调一步」，
/// 目前唯一的调用点做对了，加第二个的时候必漏。
/// </summary>
public class MemoryManager : UniquieContainerSingleton<MemoryManager, MemoryData>
{
    protected override string SaveRootPath => SettingConfig.MemoryDataPath;

    protected override void OnOrderedItems(List<MemoryData> items)
    {
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    public override void OnInitialize()
    {
        base.OnInitialize();
        MarkOutdatedIndexesDirty();
    }

    /// <summary>
    /// 删除知识库，连索引库一起清掉
    /// </summary>
    /// <param name="item">要删除的知识库</param>
    public override void Delete(MemoryData item)
    {
        MemoryStore store = item.Store;

        // 先释放再删文件:集合句柄占着的话,库文件在 Windows 上删不掉
        item.Dispose();
        try
        {
            store.DeleteAll();
        }
        catch (Exception e)
        {
            // 条目该删就得删。索引文件残留只是占磁盘,不影响正确性,下次同名新建会被覆盖
            Log.Warning($"Memory index files cleanup failed: {item.Name}, {e.Message}");
        }

        base.Delete(item);
    }

    /// <summary>
    /// 改名，并把索引库搬到新名字下
    /// </summary>
    /// <param name="item">要改名的知识库</param>
    /// <param name="newName">新名称</param>
    /// <returns>去重后的最终名称</returns>
    /// <exception cref="IOException">索引库搬迁失败,此时名称已回滚</exception>
    public override string ModifyName(MemoryData item, string newName)
    {
        string oldName = item.Name;
        item.ResetSearchState();

        // 最终名称要等基类去重后才知道("foo" 可能变成 "foo_0"),所以先改名再按最终名搬库
        string finalName = base.ModifyName(item, newName);
        if (string.Equals(oldName, finalName, StringComparison.Ordinal)) return finalName;

        try
        {
            MemoryStore.MoveDatabaseFiles(oldName, finalName);
        }
        catch (Exception e)
        {
            Log.Error($"Memory index rename failed: {oldName} -> {finalName}, {e.Message}");
            base.ModifyName(item, oldName); //库没搬成就把名字还原,不留「json 新名、库旧名」的错位
            throw;
        }

        return finalName;
    }

    public MemoryData? GetMemoryData(string name)
    {
        return ItemDictionary.GetValueOrDefault(name);
    }

    public bool TryGetMemoryData(string name, out MemoryData? memoryData)
    {
        memoryData = ItemDictionary.GetValueOrDefault(name);
        return memoryData != null;
    }

    /// <summary>
    /// 切块规则升级后，把按旧规则建的索引标脏。
    ///
    /// 旧索引仍然打得开、检索得到，只是块边界和上下文还是旧的——不标脏的话用户永远不会知道
    /// 该重建，改动等于白做。
    ///
    /// <b>只改内存不落盘</b>：这里跑在 <see cref="OnInitialize"/> 里，而基类
    /// <see cref="Singleton{T}"/> 明确禁止初始化期回读任何 <c>Instance</c>——
    /// <see cref="MemoryData.MarkIndexDirty"/> 会经 <c>MemoryManager.Instance</c> 落盘，
    /// 在这里调就是自环：Monitor 对同线程可重入，重入时快速路径仍见 null，于是无限递归重建单例
    /// （表现为启动卡死在读 json 上）。
    ///
    /// 不落盘也不丢信息：<c>IndexVersion</c> 才是真相，脏标记每次启动按它重算即可，
    /// 顺带省掉一轮启动写盘。用户真去重建时，成功路径会把新版本号一起落下来。
    /// </summary>
    private void MarkOutdatedIndexesDirty()
    {
        foreach (MemoryData memory in ItemDictionary.Values)
        {
            if (!NeedsVersionRefresh(memory)) continue;

            memory.IndexDirty = true;
            Log.Debug($"Memory index outdated by chunker version, marked dirty: {memory.Name}");
        }
    }

    /// <summary>
    /// 这份索引是否该因为切块规则升级而标脏
    /// </summary>
    /// <param name="memory">知识库</param>
    /// <returns>建过索引、版本落后、且还没被标脏时返回 True</returns>
    internal static bool NeedsVersionRefresh(MemoryData memory)
    {
        if (memory.IsIndexVersionCurrent || memory.IndexDirty) return false;
        return memory.LastIndexedAt != null; //从没建过索引,本来就该显示「从未建立」,不必再标脏
    }
}
