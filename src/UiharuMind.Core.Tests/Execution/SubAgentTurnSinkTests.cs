using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 子代理落点攒的正文。取消时 TurnDriver 会把它当成「还没落盘的那一段」补进历史，
/// 而「哪些已经落盘」的判定不能压在一个不可靠的信号上。
///
/// 线上事故：一个 turn 跑了 42 分钟、170 条消息，停止时历史里多出一条 5075 字的消息，
/// 内容 100% 是前面 68 条正文的拼接（一个新增的字都没有）。
/// </summary>
public class SubAgentTurnSinkTests
{
    /// <summary>
    /// 边界<b>不</b>清空正文。清在这里的前提是「边界恰好落在那一发正文的尾巴上」，
    /// 而仓内两处注释说这个边界可能晚于下一次调用的首条内容：真晚到时，清空会丢掉
    /// 下一发已经流出来的首段，取消后交上去的就是一句从中间开始的半截——丢内容比多写一条重复更糟。
    /// 所以它只追加，去重交给写历史前的 TrimPersistedPrefix。
    /// </summary>
    [Fact]
    public void StreamingText_KeepsTheWholeTurnAcrossBoundaries()
    {
        SubAgentTurnSink sink = new();
        sink.Apply(new TextContent("第一发的正文"));
        sink.Apply(MessageBoundaryContent.Instance); //这一发已落盘
        sink.Apply(new TextContent("第二发的正文"));

        Assert.Equal("第一发的正文第二发的正文", sink.TakeStreamingText());
    }

    [Fact]
    public void StreamingText_IsTakenOnlyOnce()
    {
        SubAgentTurnSink sink = new();
        sink.Apply(new TextContent("正文"));

        Assert.Equal("正文", sink.TakeStreamingText());
        //取走即清：同一个落点被问两次不该交出去两遍
        Assert.Null(sink.TakeStreamingText());
    }

    [Fact]
    public void StreamingText_SurvivesTheToolCallOfItsOwnCall()
    {
        //工具调用不切正文：这一段要留到这一发落盘
        SubAgentTurnSink sink = new();
        sink.Apply(new TextContent("说明要去查什么"));
        sink.Apply(new FunctionCallContent("c1", "Grep", null));

        Assert.Equal("说明要去查什么", sink.TakeStreamingText());
    }
}
