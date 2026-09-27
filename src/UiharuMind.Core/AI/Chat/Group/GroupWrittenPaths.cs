using System.Collections.Concurrent;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 成员会话写过的文件，按会话缓存一份（给产物区用，见 <see cref="GroupArtifacts"/>）。
///
/// 为什么要缓存：产物区每次刷新都要问一遍各成员写过什么，而成员会话没有界面壳钉着，
/// 冷下来历史就被卸掉（ADR 0036）。现算的话每问一次就把整份历史读回来，清扫时再整份写一遍，
/// 群一开一关就是一轮。
///
/// 口径：历史还在内存就现算（不费盘，且编辑 / 删除也算得准）；已卸掉就用缓存——
/// 卸掉期间历史不可能变（要改先得读回来），缓存只会被「卸掉之前那段」的改动弄旧，
/// 所以历史每追加或替换一次就作废那一份。编辑与删除不发这两个信号：它们只可能让缓存
/// 多留一条已删消息写过的路径，而产物区本就只列盘上还在的文件
/// </summary>
public static class GroupWrittenPaths
{
    private static readonly ConcurrentDictionary<string, Entry> Cache = new();
    private static readonly ConcurrentDictionary<string, int> Versions = new(); //历史追加 / 替换一次加一
    private static readonly ConcurrentDictionary<string, byte> Watched = new(); //已挂上历史信号的会话（本体实例不换，挂一次即可）

    /// <summary>
    /// 这个成员写过的文件（绝对路径，按第一次写的先后）
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="workspace">相对路径的根；没绑为 null</param>
    /// <returns>绝对路径</returns>
    public static IReadOnlyList<string> Of(ChatSession member, string? workspace)
    {
        string sessionId = member.SessionId;
        Watch(member);
        int version = Versions.GetValueOrDefault(sessionId);
        if (!member.IsHistoryResident && Cache.TryGetValue(sessionId, out Entry? cached)
                                      && cached.Version == version && cached.Workspace == workspace)
        {
            return cached.Paths;
        }

        // 历史可能正被执行线程追加：取一份快照再读。算的过程中又追加了就不记——那一份已经旧了
        IReadOnlyList<string> paths = GroupArtifacts.WrittenPaths(member.History.ToList(), workspace);
        if (Versions.GetValueOrDefault(sessionId) == version) Cache[sessionId] = new Entry(version, workspace, paths);
        return paths;
    }

    private static void Watch(ChatSession member)
    {
        string sessionId = member.SessionId;
        if (!Watched.TryAdd(sessionId, 0)) return;
        member.HistoryAppended += _ => Invalidate(sessionId);
        member.HistoryMessageReplaced += (_, _) => Invalidate(sessionId);
    }

    private static void Invalidate(string sessionId) => Versions.AddOrUpdate(sessionId, 1, (_, v) => v + 1);

    private sealed record Entry(int Version, string? Workspace, IReadOnlyList<string> Paths);
}
