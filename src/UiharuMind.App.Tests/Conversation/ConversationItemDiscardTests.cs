using System.Collections.ObjectModel;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 丢弃条目的顺序约束：先摘出集合再释放位图。写反不报错，只会在某一帧渲染时炸，只能靠测试守
/// </summary>
public class ConversationItemDiscardTests
{
    private readonly ObservableCollection<ConversationItemBase> _items = new();

    private sealed class ProbeItem(ObservableCollection<ConversationItemBase> owner) : ConversationItemBase
    {
        public int ReleaseCount { get; private set; }

        public bool WasStillAttachedOnRelease { get; private set; }

        public override void ReleaseImages()
        {
            ReleaseCount++;
            if (owner.Contains(this)) WasStillAttachedOnRelease = true;
        }
    }

    private ProbeItem[] Fill(int count)
    {
        ProbeItem[] probes = Enumerable.Range(0, count).Select(_ => new ProbeItem(_items)).ToArray();
        foreach (ProbeItem probe in probes) _items.Add(probe);
        return probes;
    }

    [Fact]
    public void DiscardAll_ReleasesEveryItemAfterDetaching()
    {
        ProbeItem[] probes = Fill(3);

        _items.DiscardAll();

        Assert.Empty(_items);
        Assert.All(probes, x => Assert.Equal(1, x.ReleaseCount));
        Assert.All(probes, x => Assert.False(x.WasStillAttachedOnRelease));
    }

    [Fact]
    public void DiscardRange_OnlyTouchesTheRange()
    {
        ProbeItem[] probes = Fill(5);

        _items.DiscardRange(1, 3);

        Assert.Equal([probes[0], probes[4]], _items);
        Assert.Equal([0, 1, 1, 1, 0], probes.Select(x => x.ReleaseCount));
        Assert.All(probes, x => Assert.False(x.WasStillAttachedOnRelease));
    }

    [Fact]
    public void Discard_RemovesTheTargetsWhereverTheySit()
    {
        ProbeItem[] probes = Fill(4);

        _items.Discard([probes[3], probes[1]]);

        Assert.Equal([probes[0], probes[2]], _items);
        Assert.Equal([0, 1, 0, 1], probes.Select(x => x.ReleaseCount));
        Assert.All(probes, x => Assert.False(x.WasStillAttachedOnRelease));
    }
}
