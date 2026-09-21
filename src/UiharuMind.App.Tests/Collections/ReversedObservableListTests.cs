using System.Collections;
using System.Collections.Specialized;
using UiharuMind.Shared.Collections;

namespace UiharuMind.App.Tests.Collections;

/// <summary>
/// <see cref="ReversedObservableList{T}"/> 的倒序语义。钉住的事实：
/// 最新追加的排在第 0 位、对外发的是 <c>Insert(0)</c> 通知、底层不发生搬移。
/// <para>
/// 日志面板要「最新在顶」，而直接用 <c>Insert(0, item)</c> 会在 10 万条规模上
/// 每来一条日志就搬一次整个数组。
/// </para>
/// </summary>
public class ReversedObservableListTests
{
    /// <summary>追加的那条排在最前面，先来的往后排</summary>
    [Fact]
    public void Append_NewestComesFirst()
    {
        ReversedObservableList<string> list = new();

        list.Append("first");
        list.Append("second");

        Assert.Equal(2, list.Count);
        Assert.Equal("second", list[0]);
        Assert.Equal("first", list[1]);
        Assert.Equal(["second", "first"], list.ToArray());
    }

    /// <summary>对外的通知是「插到第 0 位」，虚拟化面板据此把已有容器整体下移一格</summary>
    [Fact]
    public void Append_RaisesInsertAtZero()
    {
        ReversedObservableList<string> list = new();
        list.Append("old");

        NotifyCollectionChangedEventArgs? captured = null;
        list.CollectionChanged += (_, e) => captured = e;
        list.Append("new");

        Assert.NotNull(captured);
        Assert.Equal(NotifyCollectionChangedAction.Add, captured!.Action);
        Assert.Equal(0, captured.NewStartingIndex);
        Assert.Equal("new", captured.NewItems?[0]);
    }

    /// <summary>裁掉的是最早的那批，也就是视图里排在最末尾的那些</summary>
    [Fact]
    public void TrimOldest_DropsFromTailOfView()
    {
        ReversedObservableList<int> list = new();
        for (int i = 0; i < 5; i++) list.Append(i);

        list.TrimOldest(2);

        Assert.Equal(3, list.Count);
        Assert.Equal(4, list[0]); //最新的不受影响
        Assert.Equal(2, list[2]); //0 与 1 被裁掉了
    }

    /// <summary>虚拟化面板要的随机访问走 IList，序号与泛型索引器一致</summary>
    [Fact]
    public void IListIndexer_MatchesReversedOrder()
    {
        ReversedObservableList<string> list = new();
        list.Append("a");
        list.Append("b");

        IList asList = list;

        Assert.Equal("b", asList[0]);
        Assert.Equal(0, asList.IndexOf("b"));
        Assert.Equal(1, asList.IndexOf("a"));
    }
}
