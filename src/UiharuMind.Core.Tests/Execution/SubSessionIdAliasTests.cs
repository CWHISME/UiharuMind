using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 子会话标识的别名层：存储侧真实 ID 不动，模型侧只见前 8 位短号。
/// 短号由真实 ID 派生，反查按前缀匹配、返回全部命中，由调用方分「唯一 / 没有 / 撞号」三路处理。
/// 每个用例用独立前缀登记进全局 SessionManager，并行跑互不干扰，finally 里清掉。
/// </summary>
public class SubSessionIdAliasTests
{
    [Theory]
    [InlineData("1e44d522c1a644799bebfdd4ce1d11df", "1e44d522")]
    [InlineData("abc123", "abc123")] //本来就短的原样返回
    public void Short_TruncatesToEightOrKeepsShortIds(string full, string expected)
    {
        Assert.Equal(expected, SubSessionIdAlias.Short(full));
    }

    [Fact]
    public void Match_UniquePrefix_Hits()
    {
        using Registered run = new("alias-test-a1b2c3d4e5f6", "parent-alias");

        Assert.Equal(run.Id, Assert.Single(SubSessionIdAlias.Match("parent-alias", "alias-test")).SessionId);
    }

    /// <summary>老历史里存的是完整 ID，精确命中自身</summary>
    [Fact]
    public void Match_CompleteId_MatchesItself()
    {
        using Registered run = new("alias-self-1234", "parent-alias");

        Assert.Equal(run.Id, Assert.Single(SubSessionIdAlias.Match("parent-alias", run.Id)).SessionId);
    }

    /// <summary>模型会把短号重打成大写，NormalizeTo 不分大小写放行了，这里也得认</summary>
    [Fact]
    public void Match_IgnoresCase()
    {
        using Registered run = new("aliascase-0001", "parent-alias");

        Assert.Equal(run.Id, Assert.Single(SubSessionIdAlias.Match("parent-alias", "ALIASCASE")).SessionId);
    }

    [Fact]
    public void Match_Collision_ReturnsEveryCandidate()
    {
        using Registered first = new("alias-ab-0001", "parent-alias");
        using Registered second = new("alias-ab-0002", "parent-alias");

        Assert.Equal(2, SubSessionIdAlias.Match("parent-alias", "alias-ab").Count);
    }

    [Fact]
    public void Match_NoHit_IsEmpty()
    {
        Assert.Empty(SubSessionIdAlias.Match("parent-alias", "nope12345"));
    }

    [Fact]
    public void Match_ScopedByParent()
    {
        using Registered run = new("alias-scope-0001", "parent-a");

        Assert.Empty(SubSessionIdAlias.Match("parent-b", "alias-scope"));
        Assert.Empty(SubSessionIdAlias.Match("parent-b", run.Id)); //完整 ID 也不许跨父会话
        Assert.Single(SubSessionIdAlias.Match(null, "alias-scope")); //不限父会话则全局能查到
    }

    /// <summary>登记一个子会话，离开作用域即删</summary>
    private sealed class Registered : IDisposable
    {
        public Registered(string id, string parentId)
        {
            Id = id;
            SessionManager.Instance.Add(new ChatSession { SessionId = id, ParentSessionId = parentId });
        }

        public string Id { get; }

        public void Dispose() => SessionManager.Instance.Delete(Id);
    }
}
