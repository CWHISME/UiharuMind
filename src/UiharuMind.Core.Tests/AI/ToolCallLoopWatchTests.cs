using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 跨服务调用的退化循环检测：思考、工具调用、工具结果三者都原样重放才掐断，
/// 翻页/重试/结果在变的轮询这些合法形状不能误伤。
/// </summary>
public class ToolCallLoopWatchTests
{
    private const string Reasoning = "I'm tracing through the shader compilation flow where ...";

    private static FunctionCallContent Call(string id, string command) =>
        new(id, "Shell", new Dictionary<string, object?> { ["command"] = command });

    // 一发 = 服务调用起点 + 思考 + 一次工具调用 + 结果；callId 每发都不同，模拟真实退化现场
    private static void FeedRound(ToolCallLoopWatch watch, int index, string reasoning,
        string command = "grep -rn MarkRenderStateDirty", string result = "====\nfile found\n====",
        string callIdPrefix = "call")
    {
        watch.Observe(ServiceCallStartedContent.Instance);
        watch.Observe(new TextReasoningContent(reasoning));
        watch.Observe(Call($"{callIdPrefix}_{index}", command));
        watch.Observe(new FunctionResultContent($"result_{index}", result));
    }

    [Fact]
    public void IdenticalRounds_AreCaughtWithDistinctCallIds()
    {
        ToolCallLoopWatch watch = new("deepseek-flash");

        ToolCallLoopException e = Assert.Throws<ToolCallLoopException>(() =>
        {
            for (int i = 0; i < 10; i++) FeedRound(watch, i, Reasoning);
        });

        Assert.Contains("deepseek-flash", e.Message);
        Assert.Contains("Shell", e.Message);
        Assert.Contains(ToolCallLoopWatch.RepeatThreshold.ToString(), e.Message);
    }

    [Fact]
    public void ChangedArguments_AreNotFlagged()
    {
        ToolCallLoopWatch watch = new();

        for (int i = 0; i < 10; i++)
            FeedRound(watch, i, Reasoning, command: $"sed -n '{i * 100},{i * 100 + 99}p' file.cpp");
    }

    [Fact]
    public void ChangedResult_IsNotFlagged()
    {
        // 后台任务轮询：思考逐字相同、命令相同，但结果在变——合法等待，不能判退化
        ToolCallLoopWatch watch = new();

        for (int i = 0; i < 10; i++)
            FeedRound(watch, i, Reasoning, command: "ps aux", result: $"task running, tick {i}");
    }

    [Fact]
    public void ResultMustBeIdentical_ToFlag()
    {
        // 同样思考+命令，但每发结果不同：即便超过阈值也不触发
        ToolCallLoopWatch watch = new();
        for (int i = 0; i < 10; i++)
            FeedRound(watch, i, Reasoning, result: $"output {i}");
    }

    [Fact]
    public void ChangedReasoning_IsNotFlagged()
    {
        ToolCallLoopWatch watch = new();
        for (int i = 0; i < 10; i++) FeedRound(watch, i, Reasoning + $" step {i}");
    }

    [Fact]
    public void InlineThinkReasoning_IsCaught()
    {
        // 本地模型把 <think> 混在正文流里，走 ThinkingBoundary 分类后仍要认出退化
        ToolCallLoopWatch watch = new();
        Assert.Throws<ToolCallLoopException>(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                watch.Observe(ServiceCallStartedContent.Instance);
                watch.Observe(new TextContent($"<think>{Reasoning}</think>"));
                watch.Observe(Call($"c{i}", "grep"));
                watch.Observe(new FunctionResultContent($"r{i}", "same output"));
            }
        });
    }

    [Fact]
    public void ToolCallWithDifferentName_IsNotFlagged()
    {
        ToolCallLoopWatch watch = new();
        for (int i = 0; i < 10; i++)
        {
            watch.Observe(ServiceCallStartedContent.Instance);
            watch.Observe(new TextReasoningContent(Reasoning));
            watch.Observe(new FunctionCallContent($"c{i}", i % 2 == 0 ? "Read" : "Grep"));
            watch.Observe(new FunctionResultContent($"r{i}", "same output"));
        }
    }

    [Fact]
    public void NoToolCall_IsNotFlagged()
    {
        ToolCallLoopWatch watch = new();
        for (int i = 0; i < 10; i++)
        {
            watch.Observe(ServiceCallStartedContent.Instance);
            watch.Observe(new TextReasoningContent(Reasoning));
        }
    }

    [Fact]
    public void EmptyReasoning_IsNotFlagged()
    {
        // 加密思考/关思考时只有工具调用，与后台轮询同形，不能判退化
        ToolCallLoopWatch watch = new();
        for (int i = 0; i < 10; i++)
        {
            watch.Observe(ServiceCallStartedContent.Instance);
            watch.Observe(Call($"c{i}", "ps aux"));
            watch.Observe(new FunctionResultContent($"r{i}", "same output"));
        }
    }

    [Fact]
    public void StreamRestart_VoidsCurrentRound()
    {
        ToolCallLoopWatch watch = new();

        // 每发跑到一半被重发打断：攒下的内容与连续计数都作废，不该攒成连续相同
        for (int i = 0; i < 10; i++)
        {
            watch.Observe(ServiceCallStartedContent.Instance);
            watch.Observe(new TextReasoningContent(Reasoning));
            watch.Observe(Call($"c{i}", "grep"));
            watch.Observe(StreamRestartContent.Instance);
        }
    }

    [Fact]
    public void OneOffRepeat_BelowThreshold_IsNotFlagged()
    {
        ToolCallLoopWatch watch = new();
        for (int i = 0; i < ToolCallLoopWatch.RepeatThreshold - 1; i++)
            FeedRound(watch, i, Reasoning);
    }

    [Fact]
    public void InterleavedDifferentRound_ResetsStreak()
    {
        ToolCallLoopWatch watch = new();
        for (int i = 0; i < 20; i++)
            FeedRound(watch, i, i % 3 == 2 ? Reasoning + $" detour {i}" : Reasoning);
    }

    [Fact]
    public void ExceptionSummary_IsTruncated()
    {
        // 参数带整段文件内容时不能把全文打进错误卡/日志
        ToolCallLoopWatch watch = new();
        string huge = new('x', 4000);
        ToolCallLoopException e = Assert.Throws<ToolCallLoopException>(() =>
        {
            for (int i = 0; i < 10; i++)
                FeedRound(watch, i, Reasoning, command: huge);
        });

        Assert.DoesNotContain(huge, e.Message);
        Assert.Contains("…", e.Message);
    }
}
