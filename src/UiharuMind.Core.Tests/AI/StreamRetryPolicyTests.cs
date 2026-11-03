using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 重发决策的纯判定：分哪一类、预算还剩多少、退避多久。
///
/// <para>
/// 不经过 <see cref="LazyChatClient"/> 也测得到，是这次把判定抽出来的全部理由——
/// 原先四条分支各自都带预算（自愈一次、退化两次、断线六次），只能造一个假客户端、
/// 真等满 1 秒退避才验得了。
/// </para>
/// </summary>
public class StreamRetryPolicyTests
{
    /// <summary>四类失败都认得出来，其余一律 Fatal（不能拿去重发）</summary>
    [Fact]
    public void Classify_RecognizesEachKind()
    {
        Assert.Equal(EStreamFailureKind.MaxTokens, StreamRetryPolicy.Classify(
            new Exception("Parameter: max_tokens\nfield MaxTokens invalid, should be in [1, 8192]")));
        Assert.Equal(EStreamFailureKind.ReasoningRequirement, StreamRetryPolicy.Classify(
            new Exception("Missing required parameter: 'messages[8].reasoning_content'.")));
        Assert.Equal(EStreamFailureKind.Degenerate, StreamRetryPolicy.Classify(new DegeneratedReplyException(500)));
        Assert.Equal(EStreamFailureKind.TransportDrop, StreamRetryPolicy.Classify(new IOException("unexpected EOF")));
        Assert.Equal(EStreamFailureKind.Fatal, StreamRetryPolicy.Classify(new InvalidOperationException("boom")));
    }

    /// <summary>自愈各只做一次：做过之后再被拒就是别的问题，不能拿断线的预算去试</summary>
    [Fact]
    public void SelfHealHappensOncePerKind()
    {
        StreamRetryPlan first = StreamRetryPolicy.Plan(EStreamFailureKind.MaxTokens, new StreamRetryBudget(1, false, false, 0));
        StreamRetryPlan again = StreamRetryPolicy.Plan(EStreamFailureKind.MaxTokens, new StreamRetryBudget(2, true, false, 0));
        Assert.Equal(EStreamRetryAction.FixMaxTokens, first.Action);
        Assert.Equal(EStreamRetryAction.GiveUp, again.Action);

        first = StreamRetryPolicy.Plan(EStreamFailureKind.ReasoningRequirement, new StreamRetryBudget(1, false, false, 0));
        again = StreamRetryPolicy.Plan(EStreamFailureKind.ReasoningRequirement, new StreamRetryBudget(2, false, true, 0));
        Assert.Equal(EStreamRetryAction.LearnReasoningRequirement, first.Action);
        Assert.Equal(EStreamRetryAction.GiveUp, again.Action);
    }

    /// <summary>退化回复的预算与断线分开：第 1、2 次重发，第 3 次收口，且从不退避</summary>
    [Fact]
    public void Degenerate_HasItsOwnBudgetAndNeverWaits()
    {
        for (int retries = 0; retries < StreamRetryPolicy.MaxDegenerateRetries; retries++)
        {
            StreamRetryPlan plan = StreamRetryPolicy.Plan(EStreamFailureKind.Degenerate,
                new StreamRetryBudget(1, false, false, retries));
            Assert.Equal(EStreamRetryAction.RetryImmediately, plan.Action);
            Assert.Equal(TimeSpan.Zero, plan.Delay);
        }

        StreamRetryPlan gaveUp = StreamRetryPolicy.Plan(EStreamFailureKind.Degenerate,
            new StreamRetryBudget(1, false, false, StreamRetryPolicy.MaxDegenerateRetries));
        Assert.Equal(EStreamRetryAction.GiveUp, gaveUp.Action);
    }

    /// <summary>断流：前五次尝试都可重发，第六次收口；退避按 1/2/4/8 封顶</summary>
    [Fact]
    public void TransportDrop_RetriesWithCappedExponentialBackoff()
    {
        for (int attempt = 1; attempt < StreamRetryPolicy.MaxAttempts; attempt++)
        {
            StreamRetryPlan plan = StreamRetryPolicy.Plan(EStreamFailureKind.TransportDrop,
                new StreamRetryBudget(attempt, false, false, 0));
            Assert.Equal(EStreamRetryAction.RetryAfterDelay, plan.Action);
            Assert.True(plan.Delay > TimeSpan.Zero);
        }

        StreamRetryPlan gaveUp = StreamRetryPolicy.Plan(EStreamFailureKind.TransportDrop,
            new StreamRetryBudget(StreamRetryPolicy.MaxAttempts, false, false, 0));
        Assert.Equal(EStreamRetryAction.GiveUp, gaveUp.Action);

        Assert.Equal(TimeSpan.FromSeconds(1), StreamRetryPolicy.Delay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), StreamRetryPolicy.Delay(2));
        Assert.Equal(TimeSpan.FromSeconds(4), StreamRetryPolicy.Delay(3));
        Assert.Equal(TimeSpan.FromSeconds(8), StreamRetryPolicy.Delay(4));
        Assert.Equal(TimeSpan.FromSeconds(8), StreamRetryPolicy.Delay(9)); //封顶后不再涨
    }

    /// <summary>认不出来的失败一次都不重发</summary>
    [Fact]
    public void Fatal_IsNeverRetried()
    {
        StreamRetryPlan plan = StreamRetryPolicy.Plan(EStreamFailureKind.Fatal, new StreamRetryBudget(1, false, false, 0));
        Assert.Equal(EStreamRetryAction.GiveUp, plan.Action);
    }
}
