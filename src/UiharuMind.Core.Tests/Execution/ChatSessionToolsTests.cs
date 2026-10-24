using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// chatsession.list 的三条不变量：脑会话不列条目、冷会话读索引占用快照且<b>不 Load 本体</b>、
/// 热会话读已驻留本体。用自建的 <see cref="SessionManager"/>（不碰全局单例）跑，
/// AppPaths 已由测试基建 <c>AppDataDirectoryInitializer</c> 指向临时目录，落盘安全
/// </summary>
public sealed class ChatSessionToolsTests : IDisposable
{
    private readonly SessionManager _manager = new();
    private readonly List<string> _created = [];

    public void Dispose()
    {
        foreach (string id in _created) _manager.Delete(id);
    }

    private ChatSession CreateSub(string parentId, string title, ESubAgentType type = ESubAgentType.General,
        long tokens = 0)
    {
        ChatSession session = new()
        {
            Title = title,
            ParentSessionId = parentId,
            SubAgentType = type,
            IsAgentForm = true,
            LastInputTokens = tokens,
        };
        _manager.Add(session);
        _created.Add(session.SessionId);
        return session;
    }

    [Fact]
    public void List_ExcludesBrainSessions()
    {
        ChatSession sub = CreateSub("parent", "评审草案");
        CreateSub("parent", "整理脑", ESubAgentType.Brain);

        string text = ChatSessionTools.ListSubSessions("parent", _manager);

        Assert.Contains(SubSessionIdAlias.Short(sub.SessionId), text);
        Assert.DoesNotContain("整理脑", text); // 脑不列条目，与右栏「只报数量」同口径
    }

    [Fact]
    public void List_WithOnlyBrain_ReturnsEmptyNotice()
    {
        CreateSub("parent", "整理脑", ESubAgentType.Brain);

        string text = ChatSessionTools.ListSubSessions("parent", _manager);

        Assert.Contains("还没有派出过子代理", text);
    }

    /// <summary>探索档像通用委派一样列条目，且带「只读」标记（它改不了东西）；脑仍不列</summary>
    [Fact]
    public void List_ListsExplorerWithReadOnlyMarker()
    {
        ChatSession explorer = CreateSub("parent", "调研代码库", ESubAgentType.Explorer);
        ChatSession general = CreateSub("parent", "实现功能", ESubAgentType.General);

        string text = ChatSessionTools.ListSubSessions("parent", _manager);

        Assert.Contains(SubSessionIdAlias.Short(explorer.SessionId), text);
        // 只读标记只打在探索档那一行上，通用档不带（脑不列条目由 List_ExcludesBrainSessions 钉住）
        string explorerLine = text.Split('\n').First(x => x.Contains(SubSessionIdAlias.Short(explorer.SessionId)));
        string generalLine = text.Split('\n').First(x => x.Contains(SubSessionIdAlias.Short(general.SessionId)));
        Assert.Contains("只读", explorerLine);
        Assert.DoesNotContain("只读", generalLine);
    }

    [Fact]
    public void List_ColdSession_ReadsIndexSnapshot_WithoutLoading()
    {
        ChatSession sub = CreateSub("parent", "评审草案", tokens: 1234);

        // 第二个实例从同一档案重新初始化：本体从未加载过（GetLoaded 为 null），是冷会话
        SessionManager fresh = new();
        fresh.OnInitialize();

        string text = ChatSessionTools.ListSubSessions("parent", fresh);

        Assert.Contains(SubSessionIdAlias.Short(sub.SessionId), text);
        Assert.Contains("1.2k", text); // 占用来自索引快照（1234 → Format 简写）
        Assert.Null(fresh.GetLoaded(sub.SessionId)); // 调用之后仍是冷：list 没有 Load 本体
    }

    [Fact]
    public void List_WarmSession_ShowsOccupancyFromBody()
    {
        ChatSession sub = CreateSub("parent", "评审草案", tokens: 4321);

        // 本实例 Add 过即热：GetLoaded 非空，占用从本体读
        string text = ChatSessionTools.ListSubSessions("parent", _manager);

        Assert.Contains("4.3k", text);
    }
}