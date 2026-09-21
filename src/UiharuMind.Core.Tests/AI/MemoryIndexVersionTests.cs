using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 切块规则升级后「哪些索引该标脏」的判定。
///
/// 判错的两种后果都不响：漏标则用户永远不知道该重建，切块改动等于白做；
/// 错标则从没建过索引的库显示「已过期」，而它其实该显示「从未建立」。
///
/// 另有一条不在这里但更要紧的约束：这段判定跑在 <c>MemoryManager.OnInitialize</c> 里，
/// 因此<b>不得落盘</b>——落盘要经 <c>MemoryManager.Instance</c>，在初始化期回读单例会形成自环，
/// Monitor 同线程可重入而快速路径仍见 null，结果是无限递归重建单例、启动卡死。
/// 见 <c>Singleton&lt;T&gt;</c> 的类注释。
/// </summary>
public class MemoryIndexVersionTests
{
    /// <summary>建过索引但版本落后：该标脏,让用户看到「需要更新索引」</summary>
    [Fact]
    public void OutdatedVersionWithExistingIndex_NeedsRefresh()
    {
        var memory = new MemoryData
        {
            IndexVersion = MemoryData.CurrentIndexVersion - 1,
            LastIndexedAt = DateTime.UtcNow,
            IndexDirty = false
        };

        Assert.True(MemoryManager.NeedsVersionRefresh(memory));
    }

    /// <summary>字段出现之前建的索引（版本 0）同样落后</summary>
    [Fact]
    public void MissingVersionField_NeedsRefresh()
    {
        var memory = new MemoryData { IndexVersion = 0, LastIndexedAt = DateTime.UtcNow };

        Assert.True(MemoryManager.NeedsVersionRefresh(memory));
    }

    /// <summary>版本已是最新：不动,否则每次启动都白标一遍</summary>
    [Fact]
    public void CurrentVersion_NeedsNoRefresh()
    {
        var memory = new MemoryData
        {
            IndexVersion = MemoryData.CurrentIndexVersion,
            LastIndexedAt = DateTime.UtcNow
        };

        Assert.False(MemoryManager.NeedsVersionRefresh(memory));
        Assert.True(memory.IsIndexVersionCurrent);
    }

    /// <summary>已经是脏的：不必再标,免得把「已过期」的原因搞混</summary>
    [Fact]
    public void AlreadyDirty_NeedsNoRefresh()
    {
        var memory = new MemoryData
        {
            IndexVersion = 0,
            LastIndexedAt = DateTime.UtcNow,
            IndexDirty = true
        };

        Assert.False(MemoryManager.NeedsVersionRefresh(memory));
    }

    /// <summary>
    /// 从没建过索引：不标脏。
    /// 标了的话状态会显示「已过期」,而用户其实一次都没建过——该显示「从未建立」。
    /// </summary>
    [Fact]
    public void NeverIndexed_NeedsNoRefresh()
    {
        var memory = new MemoryData { IndexVersion = 0, LastIndexedAt = null };

        Assert.False(MemoryManager.NeedsVersionRefresh(memory));
    }

    /// <summary>新建的知识库默认不是「版本落后」——它是「从未建立」</summary>
    [Fact]
    public void FreshMemory_NeedsNoRefresh()
    {
        Assert.False(MemoryManager.NeedsVersionRefresh(new MemoryData()));
    }
}
