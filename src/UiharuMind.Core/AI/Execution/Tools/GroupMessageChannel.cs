using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// <c>SendMessage</c> 的群分支：往<b>某个群</b>里说一句。群没有「归谁所有」的概念，
/// 收件人按群名或短号在全库找——列表（<c>chatsession.list-groups</c>）给什么就能发给谁。
///
/// 发言以<b>用户的名义</b>进群流水——成员看到的就是用户说的，与化身同语义：会话是替用户在场的。
/// 句一落进流水，这一波就由群自己跑（成员照调度被叫醒/插话），<b>工具不等它跑完</b>：
/// 一整波可能跑十几分钟，主代理这一轮不该被它堵住。起一个<b>等待任务</b>听 <see cref="GroupChatCoordinator.EpisodeEnded"/>，
/// 波末把最后一句成员发言作为结果送回<b>发送方</b>（走 <see cref="SessionReportSink"/> → 唤醒轮）。
/// </summary>
internal static class GroupMessageChannel
{
    /// <summary>
    /// 收件人里方括号编号的宽容认法：模型把列表整行（<c>《名》[d5874adf]</c>）拷进 <c>to</c> 也认——
    /// 与子代理的 <c>[sub-session: …]</c> 同款（<see cref="SubAgentTool.NormalizeTo"/>）。
    /// 也认老格式 <c>[群壳 d5874adf]</c>：旧上下文里的工具结果还长那样。
    /// 多放行 <c>-</c> 与 <c>_</c> 只是防复制时带上的变体，不代表系统会发出这种标识
    /// </summary>
    private static readonly Regex BracketedRef = new(@"\[\s*(?:群壳\s*)?([A-Za-z0-9\-_]+)\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 起等待任务的默认实现：经 <see cref="BackgroundTaskRegistry.StartInline"/> 真起。
    /// 任务的<b>归属会话是发送方</b>——<see cref="SessionReportSink"/> 按归属送信，
    /// 填成群的会话的话报告会以 user 角色落进群流水，成员下次投递会把它当用户说的话读进去
    /// </summary>
    internal static readonly Action<string, ChatSession, int, GroupEpisodeWatch> DefaultStartWaiter =
        (senderSessionId, group, start, watch) =>
            BackgroundTaskRegistry.StartInline(senderSessionId, $"等《{group.Title}》这一波跑完",
                WaitTaskLimits.MaxRuntime, token => WaitEpisodeEndedAsync(group, start, watch, token),
                SessionReportSink.Instance);

    /// <summary>
    /// 起等待任务的入口。默认 <see cref="DefaultStartWaiter"/>；测试换假件记录调用。
    /// <b>假件接手了 <see cref="GroupEpisodeWatch"/> 的所有权</b>（得负责 Dispose），不接手就别替换这个口子
    /// </summary>
    internal static Action<string, ChatSession, int, GroupEpisodeWatch> StartWaiter = DefaultStartWaiter;

    /// <summary>
    /// 按短号或群名找群：一个即唯一命中，零个即没有，多个即撞名/撞号
    /// </summary>
    /// <param name="target">模型填的收件人（群名或短号）</param>
    /// <returns>命中的群元数据</returns>
    internal static IReadOnlyList<ChatSessionMeta> Match(string target)
    {
        // 先剥方括号编号：模型常把列表整行（《名》[d5874adf]）照抄进 to
        string wanted = target.Trim();
        Match bracketed = BracketedRef.Match(wanted);
        if (bracketed.Success) wanted = bracketed.Groups[1].Value;
        // 再剥书名号：回执与列表里的群名带《》，模型常把它们一起拷贝进来
        wanted = wanted.Trim().Trim('《', '》', '「', '」');
        if (wanted.Length == 0) return [];

        List<ChatSessionMeta> all = SessionManager.Instance.FindSessions(x => x.IsGroup)
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
        if (all.Count == 0) return [];

        // 短号优先：撞名报错给的是短号，模型照着填时按它认最准
        List<ChatSessionMeta> byId = all
            .Where(x => x.SessionId.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byId.Count > 0
            ? byId
            : all.Where(x => string.Equals(x.Title, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 以用户的名义往群里说一句，不等这一波跑完；起等待任务在波末把最后一句送回发送方
    /// </summary>
    /// <param name="group">群元数据</param>
    /// <param name="message">要说的话</param>
    /// <param name="senderSessionId">发送方会话标识：报告送回这里</param>
    /// <returns>给模型的回执</returns>
    internal static string Post(ChatSessionMeta group, string message, string senderSessionId)
    {
        if (SessionManager.Instance.Load(group.SessionId) is not { IsGroup: true } body)
        {
            return $"Error: 群《{group.Title}》打不开，这句没发出去。";
        }

        // 发之前先看：已经在跑就是插进那一波，否则由这句开一波——回执要把这两种说清
        bool running = SessionManager.Instance.Running.IsBusy(group.SessionId);

        // 起点（这句的落盘下标）与订阅都必须在发帖之前拿到：PostAsync 的同步段就可能把整波跑完，
        // 事后再订阅会错过那一次 EpisodeEnded，干等到超时
        int start = body.History.Count;
        GroupEpisodeWatch watch = GroupEpisodeWatch.Begin(group.SessionId);

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
            watch.Dispose();
            Log.Error($"Group post to '{group.Title}' failed: {e.Message}");
            return $"Error: 发到《{group.Title}》失败：{e.Message}。";
        }

        _ = posting.ContinueWith(t => Log.Error($"Group post to '{group.Title}' failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);

        string receipt = running
            ? $"已以用户的名义发到《{group.Title}》——那一波正在跑，这句已经插进去了。"
            : $"已以用户的名义发到《{group.Title}》，这一波开跑了。";

        // 起等待任务：波末把最后一句成员发言送回发送方（自动唤醒）
        try
        {
            StartWaiter(senderSessionId, body, start, watch);
        }
        catch (Exception e)
        {
            watch.Dispose();
            Log.Error($"Start group wait task for '{group.Title}' failed: {e.Message}");
            return receipt + "这次没能挂上回信（应用可能正在退出），别等它。";
        }

        string listing = BuiltinExtensions.Mention(BuiltinExtensions.ChatSession, BuiltinExtensions.ChatSessionGroups);
        return receipt
               + "这一波跑完会自动送一封回信（自动唤醒你，不用轮询）。"
               + $"中途想看进度用 {listing}。";
    }

    /// <summary>
    /// 等待任务的任务体：听 <see cref="GroupChatCoordinator.EpisodeEnded"/>，等群<b>真正安静下来</b>后
    /// 从流水里取最后一句成员发言作为结果。
    ///
    /// 收场的不一定是该等的那一波：这句可能在上一波收场前一刻才落盘（那一波没把它投给成员，它被排到下一波），
    /// 也可能刚收场群里又开了一波。按流水长度与「还在不在跑」认出来，接着等
    /// </summary>
    /// <param name="group">群会话（Post 时已加载的那个实例）</param>
    /// <param name="start">这句发言的落盘下标；比它早收场的一波不含这句</param>
    /// <param name="watch">发帖前就建好的订阅（本方法负责 Dispose）</param>
    /// <param name="cancellationToken">任务级取消：用户叫停等待任务或到时间上限时只结束等待，群那一波照常跑</param>
    /// <returns>送还发送方的报告正文</returns>
    internal static async Task<string> WaitEpisodeEndedAsync(ChatSession group, int start, GroupEpisodeWatch watch,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                GroupEpisodeSummary summary = await watch.NextAsync(cancellationToken).ConfigureAwait(false);
                // 收场时流水还没到我这句：这一波不含它，接着等下一波
                if (summary.End <= start) continue;
                // 刚收场又开了一波（这句被排到了新的一波）：等它也跑完，别报半截
                if (GroupChatCoordinator.Instance.IsRunning(group.SessionId)) continue;

                return BuildGroupReport(start, summary);
            }
        }
        finally
        {
            watch.Dispose();
        }
    }

    /// <summary>
    /// 把一波收场写成送回发送方的正文：最后一句成员发言（<paramref name="start"/> 与
    /// <see cref="GroupEpisodeSummary.Start"/> 之后的区间内最后一个有字的成员发言），
    /// 没发言或波被停则如实说明
    /// </summary>
    /// <param name="start">这句发言的落盘下标（区间下界，取它与波起点里靠后的那个）</param>
    /// <param name="summary">收场回顾</param>
    /// <returns>报告正文</returns>
    internal static string BuildGroupReport(int start, GroupEpisodeSummary summary)
    {
        ChatSession group = summary.Group;
        // 波末写一份定稿并直接给路径：中途被 list-groups 写下的那份只覆盖到那一刻，
        // 拿它当「这一波说了什么」读会少掉后半截。写不出（临时会话/离席中/写盘失败）才退回指路
        string hint = GroupLogFile.TrySnapshot(group) is { } log
            ? $"\n\n【这一波的完整流水：{log} ——想看谁还说了什么，读它。】"
            : "\n\n【这一波谁还说了什么，用 "
              + BuiltinExtensions.Mention(BuiltinExtensions.ChatSession, BuiltinExtensions.ChatSessionGroups)
              + " 拿流水文件路径，读它。】";
        IList<ChatMessage> history = group.History;
        int from = Math.Max(summary.Start, start);
        int to = Math.Min(summary.End, history.Count);
        for (int i = to - 1; i >= from; i--)
        {
            ChatMessage message = history[i];
            if (ChatMessageAnnotations.GroupSpeakerSessionOf(message) == null) continue;
            string text = message.Text?.Trim() ?? string.Empty;
            if (text.Length == 0) continue;

            string stop = summary.Stopped ? "\n\n（这一波被用户停下了）" : string.Empty;
            return $"《{group.Title}》这一波结束，最后一句是：\n\n"
                   + $"{BackgroundTaskReport.QuoteForReport(text)}{stop}{hint}";
        }

        return (summary.Stopped
            ? $"《{group.Title}》这一波被用户停下，成员没有留下可读的发言。"
            : $"《{group.Title}》这一波结束了，但成员没有留下可读的发言。") + hint;
    }
}
