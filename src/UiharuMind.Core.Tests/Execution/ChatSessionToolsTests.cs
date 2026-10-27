using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// chatsession.list 的三条不变量：脑会话不列条目、冷会话读索引占用快照且<b>不 Load 本体</b>、
/// 热会话读已驻留本体。用自建的 <see cref="SessionManager"/>（不碰全局单例）跑，
/// AppPaths 已由测试基建 <c>AppDataDirectoryInitializer</c> 指向临时目录，落盘安全
/// </summary>
[Collection("ChatSessionState")] //私有管理器 SaveIndex 会把共享索引并进来，见 ChatSessionStateCollection
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

    /// <summary>列最近的群聊，不按发起会话过滤（群不记发起会话）；末尾提示发群写法</summary>
    [Fact]
    public void ListGroups_ListsRecentGroups_AndHintsSendMessage()
    {
        string parent = NewParent();
        ChatSession mine = CreateGroup("list-评审群");
        CreateGroup("list-别人的群");
        CreateGroup("list-用户手建的群");

        string text = ChatSessionTools.ListGroups(parent, _manager);

        Assert.Contains(mine.Title, text);
        Assert.Contains("list-别人的群", text);
        Assert.Contains("list-用户手建的群", text);
        Assert.Contains("SendMessage", text); //提示：往群里说话走到哪个工具
    }

    [Fact]
    public void ListGroups_WithNone_SaysHowToCreateOne()
    {
        // 不往管理器里 Add 任何东西：一写索引就会把共享盘上的别的群并进来，"空"就测不了了
        string text = ChatSessionTools.ListGroups("fake-parent", _manager);

        Assert.Contains("当前没有群聊", text);
        Assert.Contains("chatsession.create-group", text);
    }

    /// <summary>列表里不出现「群壳」这种叫法：群名 + 短号，短号照旧要给（撞名时靠它挑）</summary>
    [Fact]
    public void ListGroups_ShowsNameAndShortId_WithoutTheShellJargon()
    {
        string parent = NewParent();
        ChatSession group = CreateGroup("list-干净群");

        string text = ChatSessionTools.ListGroups(parent, _manager);

        Assert.Contains("《list-干净群》", text);
        Assert.Contains(SubSessionIdAlias.Short(group.SessionId), text);
        Assert.DoesNotContain("群壳", text);
    }

    /// <summary>正在跑的群要把流水写进标准的 <c>群流水.md</c> 并把路径交回来——跑着的时候「谁说到哪了」只能从流水看</summary>
    [Fact]
    public void ListGroups_RunningGroup_WritesLogAndReturnsPath()
    {
        string parent = NewParent();
        ChatSession group = CreateGroup("list-跑着的群");
        group.History.Add(new ChatMessage(ChatRole.User, "开工吧"));

        using (_manager.Running.BeginRun(group.SessionId))
        {
            string text = ChatSessionTools.ListGroups(parent, _manager);

            string path = GroupLogFile.PathOf(group);
            Assert.True(File.Exists(path));
            Assert.Contains("开工吧", File.ReadAllText(path));
            Assert.Contains(path, text);
        }

        // 空闲的群不为它重写流水：查询不该在群里留新痕迹
        string again = GroupLogFile.PathOf(group);
        File.Delete(again);
        Assert.DoesNotContain(again, ChatSessionTools.ListGroups(parent, _manager));
        Assert.False(File.Exists(again));
    }

    /// <summary>
    /// 空闲的群<b>不</b>给流水路径：那份文件停在最后一次写入那一刻（可能只是某一波的中间），
    /// 模型拿它当「刚才说了什么」读会少掉后半截。要定稿走等待任务报告那条路（波末重写）
    /// </summary>
    [Fact]
    public void ListGroups_IdleGroup_DoesNotHandOutAStaleLogPath()
    {
        string parent = NewParent();
        ChatSession group = CreateGroup("list-闲着但有旧流水的群");
        group.History.Add(new ChatMessage(ChatRole.User, "上一波说过的话"));
        string path = GroupLogFile.PathOf(group);
        Assert.True(GroupLogFile.Initialize(group, group.History) > 0);

        string text = ChatSessionTools.ListGroups(parent, _manager);

        Assert.DoesNotContain(path, text);
    }

    /// <summary>显式 dump：空闲的群也按当前历史重写整份流水并给路径——与 list-groups「空闲不落痕」对照，这是主动要的</summary>
    [Fact]
    public void Dump_IdleGroup_WritesFullLogAndReturnsPath()
    {
        string parent = NewParent();
        ChatSession group = CreateGroup("dump-闲着的群");
        group.History.Add(new ChatMessage(ChatRole.User, "上一波说过的话"));

        string text = ChatSessionTools.Dump(parent, group.Title, _manager);

        string path = GroupLogFile.PathOf(group);
        Assert.Contains(path, text);
        Assert.True(File.Exists(path));
        string body = File.ReadAllText(path);
        Assert.Contains("dump-闲着的群", body);
        Assert.Contains("上一波说过的话", body);
    }

    /// <summary>dump 子代理：历史导成 markdown（正文 + 工具调用原样参数）落到它的草稿目录并给路径</summary>
    [Fact]
    public void Dump_SubSession_WritesSessionMarkdown()
    {
        string parent = NewParent();
        ChatSession sub = CreateSub(parent, "dump-评审草案");
        sub.History.Add(new ChatMessage(ChatRole.User, "请评审代码"));
        sub.History.Add(new ChatMessage(ChatRole.Assistant,
        [
            new TextContent("我先看看结构。"),
            new FunctionCallContent("call-1", "file.glob", new Dictionary<string, object?> { ["pattern"] = "**/*.cs" }),
        ]));

        string text = ChatSessionTools.Dump(parent, SubSessionIdAlias.Short(sub.SessionId), _manager);

        string path = Path.Combine(AgentBuildProfile.DraftRoomOf(sub),
            SessionLogText.FileNameOf(SubSessionIdAlias.Short(sub.SessionId)));
        Assert.Contains(path, text);
        Assert.True(File.Exists(path));
        string body = File.ReadAllText(path);
        Assert.Contains("dump-评审草案", body);
        Assert.Contains("请评审代码", body);
        Assert.Contains("我先看看结构。", body);
        Assert.Contains("file.glob", body);
        Assert.Contains("\"pattern\"", body); //工具参数原样导出
    }

    /// <summary>查无此群/子代理时报错，并指路去哪看有哪些</summary>
    [Fact]
    public void Dump_Unknown_ReturnsError()
    {
        string text = ChatSessionTools.Dump("fake-parent", "不存在的群", _manager);

        Assert.Contains("没有『不存在的群』", text);
        Assert.Contains("list-groups", text);
    }

    /// <summary>list-tasks 列本会话名下在跑的后台任务（含 SendMessage 自动起的等待任务）</summary>
    [Fact]
    public async Task ListTasks_ListsRunningTasks()
    {
        string owner = "list-tasks-owner";
        NoopSink sink = new();
        InlineBackgroundTask inline = BackgroundTaskRegistry.StartInline(owner, "等《评审群》这一波跑完",
            TimeSpan.FromMinutes(1), async ct => { await Task.Delay(TimeSpan.FromMinutes(5), ct); return string.Empty; }, sink);

        try
        {
            string text = ChatSessionTools.ListTasks(owner);

            Assert.Contains("等《评审群》这一波跑完", text);
            Assert.Contains(inline.Id, text);
            Assert.DoesNotContain(".log", text); //inline 任务没有日志路径
        }
        finally
        {
            BackgroundTaskRegistry.StopAllOf(owner);
        }
    }

    /// <summary>名下没有任务时如实说没有；列的是别的会话的任务不串</summary>
    [Fact]
    public void ListTasks_WithNone_SaysSo()
    {
        string text = ChatSessionTools.ListTasks("list-tasks-never");

        Assert.Contains("没有在跑的后台任务", text);
    }

    private sealed class NoopSink : IBackgroundTaskReportSink
    {
        public Task DeliverAsync(BackgroundTaskOutcome outcome) => Task.CompletedTask;

        public void DeliverOnShutdown(BackgroundTaskOutcome outcome)
        {
        }
    }

    private string NewParent()
    {
        ChatSession session = new() { Title = "主会话" };
        _manager.Add(session);
        _created.Add(session.SessionId);
        return session.SessionId;
    }

    private ChatSession CreateGroup(string title)
    {
        ChatSession group = new()
        {
            Title = title,
            Description = title,
            IsGroup = true,
            IsAgentGroup = true,
        };
        _manager.Add(group);
        // 私有管理器的 SaveIndex 会把共享盘上的群并进自己的索引；把时间戳提到未来，
        // 保证「最近五个」的断言不被并行建群的别的测试挤掉。Add 里的 SaveMeta 已把它覆盖成 now，
        // 这里改回未来再存一次（不刷新时间，只让未来值进索引）
        group.UpdatedAt = DateTimeOffset.Now.AddHours(1);
        _manager.SaveMeta(group, touchUpdatedAt: false);
        _created.Add(group.SessionId);
        return group;
    }
}