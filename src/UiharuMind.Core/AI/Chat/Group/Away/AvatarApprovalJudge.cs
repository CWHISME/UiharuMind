using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>
/// 化身判一条审批：用化身这次离席选的模型做一次旁路调用，不进它的历史、不算一轮。
/// 系统提示就是化身自己的（卡上的规矩 + 注入的用户卡），末尾再交代这一刻要做的事；
/// 上下文给用户离席以来说过的话（离席目标在最前）与群里最近的话——拍过的板已经在群里，不必把它整段历史再发一遍
/// </summary>
public static class AvatarApprovalJudge
{
    private const int RecentPosts = 20; //带上群里最近几句
    private const int MaxUserPosts = 10; //用户离席以来说的话最多带几句
    private const int MaxPostChars = 500; //每句截到多长
    private const int MaxArgumentChars = 2000; //参数截到多长
    private const string ApproveWord = "批准";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 判一条审批
    /// </summary>
    /// <param name="ask">审批</param>
    /// <param name="cancellationToken">请求方这一轮被停时取消</param>
    /// <returns>判定</returns>
    public static async Task<GroupAwayApprovalVerdict> JudgeAsync(GroupAwayApprovalAsk ask,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instructions(ask.Avatar)),
            new(ChatRole.User, Question(ask)),
        ];
        ChatResponse response = await HistorySupply.ClientFor(ask.Avatar)
            .GetResponseAsync(messages, new ChatOptions(), timeout.Token).ConfigureAwait(false);
        return Parse(response.Text);
    }

    /// <summary>
    /// 把模型的回答读成判定：第一行以「批准」开头才算批，其余一律按拒绝
    /// </summary>
    /// <param name="answer">模型回答</param>
    /// <returns>判定</returns>
    public static GroupAwayApprovalVerdict Parse(string answer)
    {
        string[] lines = answer.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return new GroupAwayApprovalVerdict(false, "化身没给出判定，按拒绝处理");

        string reason = lines.Length > 1 ? string.Join(" ", lines.Skip(1)) : lines[0];
        return new GroupAwayApprovalVerdict(lines[0].StartsWith(ApproveWord, StringComparison.Ordinal), reason);
    }

    /// <summary>
    /// 用户离席以来亲口说过的话：从最近那句离席目标起，化身替他说的不算
    /// </summary>
    /// <param name="groupLog">群流水</param>
    /// <returns>按先后，至多最近几句</returns>
    public static IReadOnlyList<ChatMessage> UserWordsSinceGoal(IReadOnlyList<ChatMessage> groupLog)
    {
        int goal = -1;
        for (int i = groupLog.Count - 1; i >= 0 && goal < 0; i--)
        {
            if (IsUserOwn(groupLog[i]) &&
                groupLog[i].Text.TrimStart().StartsWith(GroupAvatarTranscript.AwayGoalTag, StringComparison.Ordinal))
                goal = i;
        }

        return goal < 0 ? [] : groupLog.Skip(goal).Where(IsUserOwn).TakeLast(MaxUserPosts).ToList();
    }

    private static bool IsUserOwn(ChatMessage message) =>
        message.Role == ChatRole.User && ChatMessageAnnotations.GroupAvatarPostOf(message) == null &&
        ChatMessageAnnotations.GroupAwayReceiptOf(message) == null;

    private static string Instructions(ChatSession avatar)
    {
        string userName = CharacterManager.Instance.UserCharacterName;
        return CharacterPromptBuilder.Build(avatar.CharacterData) +
               $"\n\n## 现在要做的：替{userName}点一次审批\n" +
               "群里有成员要调用工具，等着批。批准的标准：这一步是在朝离席目标推进、改动落在工作区之内、出了错收得回来。" +
               "推送、大批删除、动外部账号、花钱这类收不回的操作，拒绝。\n" +
               $"只回两行：第一行「{ApproveWord}」或「拒绝」，第二行一句理由。";
    }

    private static string Question(GroupAwayApprovalAsk ask)
    {
        StringBuilder text = new();
        IReadOnlyList<ChatMessage> userWords = UserWordsSinceGoal(ask.GroupLog);
        text.Append($"{CharacterManager.Instance.UserCharacterName}离席以来说过的话（离席目标在最前，后说的优先）：\n");
        if (userWords.Count == 0) text.Append("（没找到）\n");
        foreach (ChatMessage post in userWords) text.Append(Clip(post.Text)).Append('\n');

        text.Append("\n群里最近的话：\n");
        foreach (ChatMessage post in ask.GroupLog.TakeLast(RecentPosts))
        {
            if (post.Text.Trim().Length == 0 || ChatMessageAnnotations.GroupAwayReceiptOf(post) != null) continue;
            text.Append(GroupTranscript.FormatPost(post.AuthorName, Clip(post.Text))).Append('\n');
        }

        string arguments = JsonSerializer.Serialize(ask.Call.Arguments);
        if (arguments.Length > MaxArgumentChars) arguments = arguments[..MaxArgumentChars] + "…";
        text.Append($"\n{ask.RequesterName} 请求调用 {ask.Call.Name}，参数：\n{arguments}");
        return text.ToString();
    }

    private static string Clip(string text)
    {
        string body = text.Trim();
        return body.Length > MaxPostChars ? body[..MaxPostChars] + "…" : body;
    }
}
