using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 子代理落点的<b>活职责</b>：只攒交给主代理的报告（半截正文的补存已不在本类，
/// 由 <c>TurnDriver</c> 的驱动侧缓冲承担）。这些分支在删除旧的
/// <c>TakeStreamingText</c> 测试时一起消失过，专门钉回来。
/// </summary>
public class SubAgentTurnSinkTests
{
    /// <summary>
    /// 一整发重来（断线重试/自愈重发）：失败那一发写进报告的旁白要退回去，
    /// 退回位置是 <see cref="ServiceCallStartedContent"/> 记下的水位，
    /// 而不是更早的轮首——否则重发范围会一直推到第一发的内容之后
    /// </summary>
    [Fact]
    public void StreamRestart_RollsBackTheReportToTheLastCallStart()
    {
        SubAgentTurnSink sink = new();
        sink.Apply(new TextContent("第一发正文"));
        sink.Apply(new FunctionCallContent("c1", "Grep", null)); //第一发正文到此只是旁白,报告从调用后重算
        sink.Apply(ServiceCallStartedContent.Instance); //记水位：重发退回这里
        sink.Apply(new TextContent("重发前多写的旁白"));
        sink.Apply(StreamRestartContent.Instance); //整段重来

        string report = sink.Report.Build(false);
        Assert.Contains("第一发正文", report); //水位之前的正文保住了
        Assert.DoesNotContain("重发前多写的旁白", report); //失败那一发的旁白被回滚
    }

    /// <summary>
    /// 插话分两种来源（用户 / 派活方），报告里要分开交代：一个都不能漏、不能串
    /// </summary>
    [Fact]
    public void Interjections_AreFlaggedSeparately()
    {
        SubAgentTurnSink sink = new();
        ChatMessage user = new(ChatRole.User, "用户的插话");
        ChatMessage parent = new(ChatRole.User, "派活方的插话");
        ChatMessageAnnotations.MarkParentInterjection(parent);

        sink.Apply(new UserMessageContent(user, isInterjection: true));
        sink.Apply(new UserMessageContent(parent, isInterjection: true));

        Assert.True(sink.SawUserInterjection);
        Assert.True(sink.SawParentInterjection);
    }

    /// <summary>本轮开头的输入不是插话：两个标记都该保持干净</summary>
    [Fact]
    public void NonInterjection_LeavesTheFlagsClean()
    {
        SubAgentTurnSink sink = new();
        sink.Apply(new UserMessageContent(new ChatMessage(ChatRole.User, "委派内容")));

        Assert.False(sink.SawUserInterjection);
        Assert.False(sink.SawParentInterjection);
    }
}