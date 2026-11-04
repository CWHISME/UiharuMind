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
        Assert.Equal(EStreamFailureKind.ProviderSafety, StreamRetryPolicy.Classify(
            new System.Net.Http.HttpRequestException("HTTP 400 (failed_precondition_error: 9)\n\nsafety check failed",
                null, System.Net.HttpStatusCode.BadRequest)));
        Assert.Equal(EStreamFailureKind.Fatal, StreamRetryPolicy.Classify(new InvalidOperationException("boom")));
    }

    /// <summary>自愈各只做一次：做过之后再被拒就是别的问题，不能拿断线的预算去试</summary>
    [Fact]
    public void SelfHealHappensOncePerKind()
    {
        StreamRetryPlan first = StreamRetryPolicy.Plan(EStreamFailureKind.MaxTokens, new StreamRetryBudget(false, false, 0));
        StreamRetryPlan again = StreamRetryPolicy.Plan(EStreamFailureKind.MaxTokens, new StreamRetryBudget(true, false, 0));
        Assert.Equal(EStreamRetryAction.FixMaxTokens, first.Action);
        Assert.Equal(EStreamRetryAction.GiveUp, again.Action);

        first = StreamRetryPolicy.Plan(EStreamFailureKind.ReasoningRequirement, new StreamRetryBudget(false, false, 0));
        again = StreamRetryPolicy.Plan(EStreamFailureKind.ReasoningRequirement, new StreamRetryBudget(false, true, 0));
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
                new StreamRetryBudget(false, false, retries));
            Assert.Equal(EStreamRetryAction.RetryImmediately, plan.Action);
            Assert.Equal(TimeSpan.Zero, plan.Delay);
        }

        StreamRetryPlan gaveUp = StreamRetryPolicy.Plan(EStreamFailureKind.Degenerate,
            new StreamRetryBudget(false, false, StreamRetryPolicy.MaxDegenerateRetries));
        Assert.Equal(EStreamRetryAction.GiveUp, gaveUp.Action);
    }

    /// <summary>断流：五次重发都可发，之后收口；退避按 1/2/4/8 封顶，且只数断线次数</summary>
    [Fact]
    public void TransportDrop_RetriesWithCappedExponentialBackoff()
    {
        for (int retries = 0; retries < StreamRetryPolicy.MaxTransportRetries; retries++)
        {
            StreamRetryPlan plan = StreamRetryPolicy.Plan(EStreamFailureKind.TransportDrop,
                new StreamRetryBudget(false, false, 0, TransportRetries: retries));
            Assert.Equal(EStreamRetryAction.RetryAfterDelay, plan.Action);
            Assert.True(plan.Delay > TimeSpan.Zero);
        }

        StreamRetryPlan gaveUp = StreamRetryPolicy.Plan(EStreamFailureKind.TransportDrop,
            new StreamRetryBudget(false, false, 0, TransportRetries: StreamRetryPolicy.MaxTransportRetries));
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
        StreamRetryPlan plan = StreamRetryPolicy.Plan(EStreamFailureKind.Fatal, new StreamRetryBudget(false, false, 0));
        Assert.Equal(EStreamRetryAction.GiveUp, plan.Action);
    }

    /// <summary>
    /// 上游安全检查拒绝：原样重发一次，第二次被拒就收口——这一码可能是真判决，
    /// 多试只是再白付一次上下文
    /// </summary>
    [Fact]
    public void ProviderSafety_ResendsOnceThenGivesUp()
    {
        StreamRetryPlan first = StreamRetryPolicy.Plan(EStreamFailureKind.ProviderSafety,
            new StreamRetryBudget(false, false, 0));
        Assert.Equal(EStreamRetryAction.RetryRejectedRequest, first.Action);
        Assert.True(first.Delay > TimeSpan.Zero);

        StreamRetryPlan again = StreamRetryPolicy.Plan(EStreamFailureKind.ProviderSafety,
            new StreamRetryBudget(false, false, 0, SafetyRetries: StreamRetryPolicy.MaxSafetyRetries));
        Assert.Equal(EStreamRetryAction.GiveUp, again.Action);
    }

    /// <summary>
    /// 三份预算各记各的：上游审查重发过不等于断线额度少了，断线重发过也不等于审查那一发没了
    /// </summary>
    [Fact]
    public void SafetyAndTransportBudgets_DoNotConsumeEachOther()
    {
        StreamRetryPlan dropped = StreamRetryPolicy.Plan(EStreamFailureKind.TransportDrop,
            new StreamRetryBudget(false, false, 0, SafetyRetries: StreamRetryPolicy.MaxSafetyRetries));
        Assert.Equal(EStreamRetryAction.RetryAfterDelay, dropped.Action);
        Assert.Equal(TimeSpan.FromSeconds(1), dropped.Delay); //退避指数只数断线，不被别的重发推高

        StreamRetryPlan rejected = StreamRetryPolicy.Plan(EStreamFailureKind.ProviderSafety,
            new StreamRetryBudget(false, false, 0, TransportRetries: StreamRetryPolicy.MaxTransportRetries));
        Assert.Equal(EStreamRetryAction.RetryRejectedRequest, rejected.Action);
    }
}
