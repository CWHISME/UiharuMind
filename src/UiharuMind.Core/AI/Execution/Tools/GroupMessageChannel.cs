using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// <c>SendMessage</c> 的群分支：往<b>某个群</b>里说一句。群没有「归谁所有」的概念，
/// 收件人按群名或群壳短号在全库找——列表（<c>chatsession.list-groups</c>）给什么就能发给谁。
///
/// 发言以<b>用户的名义</b>进群流水——成员看到的就是用户说的，与化身同语义：会话是替用户在场的。
/// 句一落进流水，这一波就由群自己跑（成员照调度被叫醒/插话），<b>工具不等它跑完</b>：
/// 一整波可能跑十几分钟，主代理这一轮不该被它堵住。进度去 <c>chatsession.list-groups</c> 看。
/// </summary>
internal static class GroupMessageChannel
{
    /// <summary>
    /// 按群壳短号或群名找群：一个即唯一命中，零个即没有，多个即撞名/撞号
    /// </summary>
    /// <param name="target">模型填的收件人（群名或群壳短号）</param>
    /// <returns>命中的群壳元数据</returns>
    internal static IReadOnlyList<ChatSessionMeta> Match(string target)
    {
        // 回执与列表里的群名带书名号，模型常把它们一起拷进来；剥掉再认
        string wanted = target.Trim().Trim('《', '》', '「', '」');
        if (wanted.Length == 0) return [];

        List<ChatSessionMeta> all = SessionManager.Instance.FindSessions(x => x.IsGroup)
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
        if (all.Count == 0) return [];

        // 短号优先：list-groups 给的是短号，模型手里多半是它
        List<ChatSessionMeta> byId = all
            .Where(x => x.SessionId.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byId.Count > 0
            ? byId
            : all.Where(x => string.Equals(x.Title, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 以用户的名义往群里说一句，不等这一波跑完
    /// </summary>
    /// <param name="group">群壳元数据</param>
    /// <param name="message">要说的话</param>
    /// <returns>给模型的回执</returns>
    internal static string Post(ChatSessionMeta group, string message)
    {
        if (SessionManager.Instance.Load(group.SessionId) is not { IsGroup: true } body)
        {
            return $"Error: 群《{group.Title}》打不开，这句没发出去。";
        }

        // 发之前先看：已经在跑就是插进那一波，否则由这句开一波——回执要把这两种说清
        bool running = SessionManager.Instance.Running.IsBusy(group.SessionId);

        // PostAsync 的同步段（追加进流水、广播开波）就在这句调用里做完，只有「这一波跑完」是后话。
        // 同步段异常会冒到工具调用上毁掉主代理这一轮：包住它，异常就当场报错；
        // 异步段（这一波跑完）的异常没人接得住，记日志即可
        Task posting;
        try
        {
            posting = GroupChatCoordinator.Instance.PostAsync(body, message);
        }
        catch (Exception e)
        {
            Log.Error($"Group post to '{group.Title}' failed: {e.Message}");
            return $"Error: 发到《{group.Title}》失败：{e.Message}。";
        }

        _ = posting.ContinueWith(t => Log.Error($"Group post to '{group.Title}' failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);

        string listing = BuiltinExtensions.Mention(BuiltinExtensions.ChatSession, BuiltinExtensions.ChatSessionGroups);
        return (running
                   ? $"已以用户的名义发到《{group.Title}》——那一波正在跑，这句已经插进去了。"
                   : $"已以用户的名义发到《{group.Title}》，这一波开跑了。")
               + $"不用等它跑完：进度用 {listing} 看（正在跑时它会给你流水快照路径）。";
    }
}
