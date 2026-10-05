using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.CrossProcess;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Instances;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 同一档案两个实例的会话互通（ADR 0064）。两个独立的 <see cref="SessionManager"/> 共用测试档案目录，
/// 就是两个进程看到的样子
/// </summary>
public sealed class SessionCrossInstanceTests : IDisposable
{
    private readonly SessionManager _mine = new();
    private readonly SessionManager _other = new();
    private readonly List<string> _created = [];

    public void Dispose()
    {
        foreach (string id in _created) _other.Delete(id);
    }

    [Fact]
    public void IndexWrite_KeepsSessionsCreatedByTheOtherInstance()
    {
        ChatSession theirs = Create(_other, "theirs");
        ChatSession ours = Create(_mine, "ours");

        List<ChatSessionMeta> index = SaveUtility.Load<List<ChatSessionMeta>>(
            Path.Combine(AppPaths.Data.Sessions, "index.json"), SessionJsonOptions.Default)!;
        Assert.Contains(index, x => x.SessionId == theirs.SessionId);
        Assert.Contains(index, x => x.SessionId == ours.SessionId);
    }

    [Fact]
    public void StaleCopy_IsNotWrittenOverTheOtherInstancesMessages()
    {
        ChatSession mine = Create(_mine, "shared");
        AdvanceElsewhere(mine.SessionId, "from the other instance");

        mine.History.Add(new ChatMessage(ChatRole.User, "stale write"));
        _mine.Save(mine);

        Assert.True(_mine.IsStaleOnDisk(mine.SessionId));
        string history = string.Join("\n", SessionManager.ReadHistoryLines(mine.SessionId));
        Assert.Contains("from the other instance", history);
        Assert.DoesNotContain("stale write", history);
    }

    [Fact]
    public void ExternalChange_DropsAnUnheldCopy_SoTheNextLoadIsFresh()
    {
        ChatSession mine = Create(_mine, "shared");
        AdvanceElsewhere(mine.SessionId, "from the other instance");
        bool notified = false;
        _mine.OnSessionsChangedExternally += () => notified = true;

        _mine.ApplyExternalChanges([mine.SessionId]);

        Assert.True(notified);
        Assert.Null(_mine.GetLoaded(mine.SessionId));
        ChatSession reloaded = _mine.Load(mine.SessionId)!;
        Assert.Contains(reloaded.History, x => x.Text == "from the other instance");
    }

    [Fact]
    public void ExternalChange_OnAPinnedSession_KeepsTheInstanceAndRefusesTurns_UntilReleased()
    {
        ChatSession mine = Create(_mine, "shared");
        IDisposable pin = _mine.Pin(mine.SessionId);
        AdvanceElsewhere(mine.SessionId, "from the other instance");

        _mine.ApplyExternalChanges([mine.SessionId]);

        Assert.Same(mine, _mine.GetLoaded(mine.SessionId)); //打开着的视图不换实例
        Assert.Equal(ETurnBlock.StaleCopy, _mine.CheckTurnBlock(mine)); //界面发送前就能拦下,不画气泡
        Assert.Null(_mine.TryClaimTurn(mine, out ETurnBlock refusal));
        Assert.Equal(ETurnBlock.StaleCopy, refusal);

        pin.Dispose(); //切走
        ChatSession reopened = _mine.Load(mine.SessionId)!;
        Assert.NotSame(mine, reopened);
        Assert.Contains(reopened.History, x => x.Text == "from the other instance");
        using IDisposable? claim = _mine.TryClaimTurn(reopened, out _);
        Assert.NotNull(claim);
    }

    [Fact]
    public void OwnWrites_AreNotTreatedAsExternal()
    {
        ChatSession mine = Create(_mine, "mine");
        bool notified = false;
        _mine.OnSessionsChangedExternally += () => notified = true;

        _mine.ApplyExternalChanges([mine.SessionId]);

        Assert.False(notified);
        Assert.Same(mine, _mine.GetLoaded(mine.SessionId));
    }

    [Fact]
    public void ExternalDelete_RemovesTheSessionFromTheList()
    {
        ChatSession mine = Create(_mine, "doomed");
        _other.Delete(mine.SessionId);

        _mine.ApplyExternalChanges([mine.SessionId]);

        Assert.Null(_mine.GetMeta(mine.SessionId));
    }

    [Fact]
    public void HeaderOnlyChangeElsewhere_DoesNotStaleTheRunningSide()
    {
        // 实机踩到的:另一边只是打开看看,切走时写了一次草稿(会话头),这边正等子代理交回的主会话就被判旧,报告写不进、唤醒轮被拒
        ChatSession mine = Create(_mine, "running here");
        ChatSession viewed = _other.Load(mine.SessionId)!;
        viewed.ComposerDraft = "typed but not sent";
        _other.SaveMeta(viewed, touchUpdatedAt: false);

        _mine.ApplyExternalChanges([mine.SessionId]);

        Assert.False(_mine.IsOutdated(mine));
        int before = mine.History.Count;
        mine.History.Add(new ChatMessage(ChatRole.Assistant, "sub-agent report"));
        _mine.Append(mine, before);
        Assert.Contains("sub-agent report", string.Join("\n", SessionManager.ReadHistoryLines(mine.SessionId)));
    }

    [Fact]
    public void ReplacedCopy_CannotWriteBack()
    {
        ChatSession old = Create(_mine, "shared");
        AdvanceElsewhere(old.SessionId, "from the other instance");
        _mine.ApplyExternalChanges([old.SessionId]); //没人持有:摘掉,下次取用读盘
        ChatSession fresh = _mine.Load(old.SessionId)!;

        // 摘的那一刻恰好有人攥着旧本体:它再写不能把新读的那份覆盖掉
        old.History.Add(new ChatMessage(ChatRole.User, "written through the replaced copy"));
        _mine.Save(old);

        Assert.True(_mine.IsOutdated(old));
        Assert.False(_mine.IsOutdated(fresh));
        Assert.DoesNotContain("written through the replaced copy",
            string.Join("\n", SessionManager.ReadHistoryLines(old.SessionId)));
    }

    [Fact]
    public void SessionRunningElsewhere_RefusesWritesTurnsAndDelete()
    {
        ChatSession mine = Create(_mine, "running there");
        // 另起一个独占句柄就是别的进程持着租约的样子(本进程的租约记账里没有它)
        using IDisposable? theirLease =
            ExclusiveFileLock.TryAcquire(Path.Combine(AppInstance.RunDirectory, "leases", mine.SessionId + ".lock"));
        Assert.NotNull(theirLease);

        mine.History.Add(new ChatMessage(ChatRole.User, "edited here meanwhile"));
        _mine.Save(mine);

        Assert.Equal(ETurnBlock.RunningElsewhere, _mine.CheckTurnBlock(mine));
        Assert.Null(_mine.TryClaimTurn(mine, out _));
        Assert.False(_mine.Delete(mine.SessionId));
        Assert.DoesNotContain("edited here meanwhile", string.Join("\n", SessionManager.ReadHistoryLines(mine.SessionId)));
    }

    [Fact]
    public void LoadingASessionRunningElsewhere_DoesNotCloseItsInFlightToolCalls()
    {
        // 实机踩到的:另一边每打开一次,就把这边还在等结果的调用当成崩溃残局补上「已中断」
        ChatSession mine = Create(_mine, "running there");
        int before = mine.History.Count;
        mine.History.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Shell")]));
        _mine.Append(mine, before);
        int onDisk = mine.History.Count;

        using (IDisposable? theirLease = ExclusiveFileLock.TryAcquire(
                   Path.Combine(AppInstance.RunDirectory, "leases", mine.SessionId + ".lock")))
        {
            Assert.NotNull(theirLease);
            Assert.Equal(onDisk, _other.Load(mine.SessionId)!.History.Count);
        }

        // 锁放了就是那边死了:这时才是残局,照常补齐
        SessionManager afterCrash = new();
        Assert.Equal(onDisk + 1, afterCrash.Load(mine.SessionId)!.History.Count);
    }

    [Fact]
    public void DeletedSession_IsNotWrittenBackBySomeoneStillHoldingIt()
    {
        // 收尾中的子代理攥着本体直接 SaveMeta:不拦就把删掉的会话写回盘上、写回索引
        ChatSession doomed = Create(_mine, "doomed");
        Assert.True(_mine.Delete(doomed.SessionId));

        doomed.Title = "written after delete";
        _mine.SaveMeta(doomed);

        Assert.False(File.Exists(Path.Combine(AppPaths.Data.Sessions, doomed.SessionId + ".meta.json")));
        Assert.Null(_mine.GetMeta(doomed.SessionId));
    }

    [Fact]
    public void Delete_IsRefusedWhole_WhenASubSessionRunsElsewhere()
    {
        ChatSession parent = Create(_mine, "parent");
        ChatSession child = new("child", new CharacterData { CharacterName = "X" }) { ParentSessionId = parent.SessionId };
        child.History.Add(new ChatMessage(ChatRole.User, "hi"));
        _mine.Add(child);
        _created.Add(child.SessionId);

        using (IDisposable? theirLease = ExclusiveFileLock.TryAcquire(
                   Path.Combine(AppInstance.RunDirectory, "leases", child.SessionId + ".lock")))
        {
            Assert.NotNull(theirLease);
            Assert.False(_mine.Delete(parent.SessionId)); //删到一半才撞上,父会话没了子会话就成孤儿
        }

        Assert.NotNull(_mine.GetMeta(parent.SessionId));
        Assert.NotNull(_mine.GetMeta(child.SessionId));
    }

    [Fact]
    public void OwnWrites_AreNeverMistakenForAnotherInstances_WhileOtherThreadsCheck()
    {
        //写完到记下指纹之间若被别的线程判旧(存会话头、Load 都会判),自己刚写的就被当成别人写的,
        //会话从此写不进盘(实机:一轮收尾追加完历史,紧接着的会话头保存被拒)
        ChatSession mine = Create(_mine, "busy");
        using CancellationTokenSource stop = new();
        int staleSeen = 0;
        Task checker = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (_mine.IsStaleOnDisk(mine.SessionId)) Interlocked.Exchange(ref staleSeen, 1);
            }
        });

        for (int i = 0; i < 300 && Volatile.Read(ref staleSeen) == 0; i++)
        {
            int before = mine.History.Count;
            mine.History.Add(new ChatMessage(ChatRole.Assistant, "reply " + i));
            _mine.Append(mine, before);
        }

        stop.Cancel();
        checker.Wait(TestContext.Current.CancellationToken);
        Assert.Equal(0, staleSeen);
    }

    private ChatSession Create(SessionManager manager, string title)
    {
        ChatSession session = new(title, new CharacterData { CharacterName = "X" });
        session.History.Add(new ChatMessage(ChatRole.User, "hello"));
        manager.Add(session);
        _created.Add(session.SessionId);
        return session;
    }

    private void AdvanceElsewhere(string sessionId, string text)
    {
        ChatSession copy = _other.Load(sessionId)!;
        int before = copy.History.Count;
        copy.History.Add(new ChatMessage(ChatRole.Assistant, text));
        _other.Append(copy, before);
    }
}
