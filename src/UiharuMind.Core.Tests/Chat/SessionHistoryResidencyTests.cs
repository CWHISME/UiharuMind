using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 冷历史卸载（ADR 0036）的记账：历史已经卸掉的会话再被取一次本体，不能又记成「历史在内存」——
/// 否则下一次清扫为了卸它先把整份历史读回来、再整份写盘，周而复始；多出来的那几笔还会把
/// 「超出上限几个」算多，让真正驻留着的会话被提前卸掉。
/// 用独立实例而不是单例：要拨时钟越过冷却，不能扰动并行跑的其它测试
/// </summary>
public sealed class SessionHistoryResidencyTests : IDisposable
{
    private readonly SessionManager _manager = new();
    private readonly List<string> _created = [];
    private DateTime _now = DateTime.UtcNow;

    public SessionHistoryResidencyTests()
    {
        _manager.UtcClock = () => _now;
    }

    public void Dispose()
    {
        foreach (string id in _created) _manager.Delete(id);
    }

    [Fact]
    public void LoadingAnUnloadedSession_DoesNotEvictAResidentOne()
    {
        List<ChatSession> sessions = CreateSessions(SessionResidencyPolicy.MaxResidentHistories + 2);
        UnloadOverflow(sessions);

        _manager.Load(sessions[0].SessionId); //只取本体（取标题、查成员），不碰历史

        // 记错成驻留的话，那一笔把「超出上限」算多一个，当场卸掉一个真正驻留着的
        Assert.True(sessions[2].IsHistoryResident);
        Assert.Equal(sessions.Count(x => x.IsHistoryResident), _manager.ResidentHistoryCount);
    }

    [Fact]
    public void Sweep_NeverReloadsAnUnloadedHistoryJustToUnloadIt()
    {
        List<ChatSession> sessions = CreateSessions(SessionResidencyPolicy.MaxResidentHistories + 2);
        UnloadOverflow(sessions);
        string historyPath = Path.Combine(AppPaths.Data.Sessions, sessions[0].SessionId + ".history.jsonl");
        DateTime stamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(historyPath, stamp);

        // 取一次已卸掉的那个，其余驻留的随后都被访问过：它若被记回驻留表，就是最冷的那一笔
        _manager.Load(sessions[0].SessionId);
        _now += TimeSpan.FromSeconds(1);
        foreach (ChatSession session in sessions.Skip(3)) _manager.Load(session.SessionId);
        CreateSessions(1); //再压一个，清扫才有得挑
        _now += TimeSpan.FromMinutes(2);
        _manager.Load(sessions[^1].SessionId);

        Assert.False(sessions[0].IsHistoryResident);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(historyPath)); //没被读回来再整份写一遍
    }

    private List<ChatSession> CreateSessions(int count)
    {
        List<ChatSession> sessions = [];
        for (int i = 0; i < count; i++)
        {
            ChatSession session = new($"residency-{i}", new CharacterData { CharacterName = "R" });
            session.History.Add(new ChatMessage(ChatRole.User, $"hello {i}"));
            _manager.Add(session);
            _created.Add(session.SessionId);
            sessions.Add(session);
            _now += TimeSpan.FromSeconds(1); //错开访问时刻，最先建的最冷
        }

        return sessions;
    }

    /// <summary>越过冷却再取一次本体，触发清扫：超出上限的两个最冷的（0、1 号）历史被卸掉</summary>
    private void UnloadOverflow(List<ChatSession> sessions)
    {
        _now += TimeSpan.FromMinutes(2);
        _manager.Load(sessions[^1].SessionId);
        Assert.Equal(SessionResidencyPolicy.MaxResidentHistories, _manager.ResidentHistoryCount);
        Assert.False(sessions[0].IsHistoryResident);
        Assert.False(sessions[1].IsHistoryResident);
    }
}
