using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// SendMessage 发群的回信正文：波末把<b>最后一句成员发言</b>交回去。
/// 只测正文组装（<see cref="GroupMessageChannel.BuildGroupReport"/>）——等待本身挂在
/// 全局 <see cref="GroupChatCoordinator"/> 上，起一波要真跑成员
/// </summary>
public sealed class GroupMessageChannelTests
{
    [Fact]
    public void BuildGroupReport_TakesTheLastMemberPost()
    {
        ChatSession group = NewGroup();
        group.History.Add(new ChatMessage(ChatRole.User, "用户说的话"));
        group.History.Add(MemberPost("初春", "第一句"));
        group.History.Add(MemberPost("佐天", "最后一句"));

        string report = GroupMessageChannel.BuildGroupReport(0, Summary(group, 0, 3, stopped: false));

        Assert.Contains("这一波结束", report);
        Assert.Contains("最后一句", report);
        Assert.DoesNotContain("第一句", report);
        Assert.DoesNotContain("用户说的话", report);
    }

    /// <summary>只认我发帖之后那一段：更早的成员发言不能当成这一波的结论</summary>
    [Fact]
    public void BuildGroupReport_OnlyLooksAfterThePostThatStartedIt()
    {
        ChatSession group = NewGroup();
        group.History.Add(MemberPost("初春", "上一波的话"));

        string report = GroupMessageChannel.BuildGroupReport(1, Summary(group, 0, 1, stopped: false));

        Assert.DoesNotContain("上一波的话", report);
        Assert.Contains("没有留下可读的发言", report);
    }

    /// <summary>化身替用户说的不算成员发言</summary>
    [Fact]
    public void BuildGroupReport_SkipsAvatarPosts()
    {
        ChatSession group = NewGroup();
        group.History.Add(MemberPost("初春", "成员的话"));
        ChatMessage avatar = new(ChatRole.User, "化身替用户说的");
        ChatMessageAnnotations.MarkGroupAvatarPost(avatar, "avatar-session");
        group.History.Add(avatar);

        string report = GroupMessageChannel.BuildGroupReport(0, Summary(group, 0, 2, stopped: false));

        Assert.Contains("成员的话", report);
        Assert.DoesNotContain("化身替用户说的", report);
    }

    [Fact]
    public void BuildGroupReport_WhenStopped_SaysSo()
    {
        ChatSession group = NewGroup();
        group.History.Add(MemberPost("初春", "说到一半"));

        string report = GroupMessageChannel.BuildGroupReport(0, Summary(group, 0, 1, stopped: true));

        Assert.Contains("说到一半", report);
        Assert.Contains("被用户停下", report);
    }

    [Fact]
    public void BuildGroupReport_WithNothingReadable_SaysSo()
    {
        ChatSession group = NewGroup();

        string report = GroupMessageChannel.BuildGroupReport(0, Summary(group, 0, 0, stopped: true));

        Assert.Contains("被用户停下", report);
        Assert.Contains("没有留下可读的发言", report);
    }

    /// <summary>正文进围栏：这条报告是 user 角色，成员的话里带反引号不能截断卡片</summary>
    [Fact]
    public void BuildGroupReport_FencesTheMemberText()
    {
        ChatSession group = NewGroup();
        group.History.Add(MemberPost("初春", "```\ncode\n```"));

        string report = GroupMessageChannel.BuildGroupReport(0, Summary(group, 0, 1, stopped: false));

        Assert.Contains("````", report);
    }

    private static ChatSession NewGroup() => new() { Title = "评审群", IsGroup = true, IsAgentGroup = true };

    private static ChatMessage MemberPost(string name, string text)
    {
        ChatMessage message = new(ChatRole.Assistant, text);
        ChatMessageAnnotations.MarkGroupPost(message, name, $"{name}-session");
        return message;
    }

    private static GroupEpisodeSummary Summary(ChatSession group, int start, int end, bool stopped) =>
        new(group, KickoffPostIndex: start, Start: start, End: end, Stopped: stopped);
}
