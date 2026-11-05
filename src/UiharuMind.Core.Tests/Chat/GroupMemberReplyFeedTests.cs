using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 成员「说完了 = 发言」的判定（ADR 0060 修订）：普通 assistant 正文进群；
/// 记忆压缩/清空记忆是给模型自己看的内部卡片，不算对群里说的话，不能发进群
/// </summary>
public class GroupMemberReplyFeedTests
{
    [Fact]
    public void OrdinaryAssistantText_IsPostedToTheGroup()
    {
        ChatSession member = new() { IsTransient = true };
        List<string> posted = [];
        using GroupMemberReplyFeed feed = new(member, Push(posted));
        member.History.Add(member.CreateMessage(ChatRole.Assistant, "这是正常发言"));
        feed.Finish(completed: true);

        Assert.Equal(["这是正常发言"], posted);
    }

    [Fact]
    public void HandoffNote_IsNotPostedToTheGroup()
    {
        ChatSession member = new() { IsTransient = true };
        List<string> posted = [];
        using GroupMemberReplyFeed feed = new(member, Push(posted));
        member.History.Add(HistoryHandoff.CreateNote("整理过的记忆正文"));
        feed.Finish(completed: true);

        Assert.Empty(posted);
    }

    [Fact]
    public void ClearNote_IsNotPostedToTheGroup()
    {
        ChatSession member = new() { IsTransient = true };
        List<string> posted = [];
        using GroupMemberReplyFeed feed = new(member, Push(posted));
        member.History.Add(HistoryHandoff.CreateClearNote(HistoryHandoff.ComposeClearBody(null)));
        feed.Finish(completed: true);

        Assert.Empty(posted);
    }

    [Fact]
    public void HandoffNote_ThenOrdinaryReply_PostsOnlyTheReply()
    {
        ChatSession member = new() { IsTransient = true };
        List<string> posted = [];
        using GroupMemberReplyFeed feed = new(member, Push(posted));
        // 真实时序：压缩发生在成员这轮末尾，note 与回复都在 feed 盯的范围内
        member.History.Add(HistoryHandoff.CreateNote("整理过的记忆正文"));
        member.History.Add(member.CreateMessage(ChatRole.Assistant, "针对新情况的回复"));
        feed.Finish(completed: true);

        Assert.Equal(["针对新情况的回复"], posted);
    }

    private static Func<string, DateTimeOffset?, Task?> Push(List<string> posted) =>
        (text, _) =>
        {
            posted.Add(text);
            return Task.CompletedTask;
        };
}