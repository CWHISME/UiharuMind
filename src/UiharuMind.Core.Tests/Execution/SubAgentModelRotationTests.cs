using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 同一档配多个模型时依次取用（ADR 0111）。
///
/// 钉住的是三件事：<b>按配置顺序轮换</b>（不是每趟都取第一个——那正是并发子代理全挤在
/// 一个模型上的原因）、<b>跳过钉不住的</b>（本地模型没起来/名字写错不该让整档回落）、
/// 以及<b>档与档之间互不干扰</b>（通用档的游标推进不该把探索档往前带）。
///
/// 游标是进程级静态状态，所以本类与 <see cref="SubAgentModelChooserTests"/> 同属一个
/// xUnit collection（并行跑会互相抢同一个游标，变成随机失败）。
/// </summary>
[Collection("SubAgentModelRotation")]
public class SubAgentModelRotationTests
{
    private static readonly string[] TwoModels = { "model-a", "model-b" };

    /// <summary>钉得住的判据：这里用一个集合模拟"跑得起来的模型"</summary>
    private static Func<string, string?> Only(params string[] usable) =>
        name => usable.Contains(name) ? name : null;

    public SubAgentModelRotationTests() => SubAgentModelRotation.Reset();

    /// <summary>两趟派活落两个模型：这就是「同时开几个子代理不卡」的那一下</summary>
    [Fact]
    public void Next_RotatesInConfiguredOrder()
    {
        string? first = SubAgentModelRotation.Next(ESubAgentType.General, TwoModels, Only(TwoModels));
        string? second = SubAgentModelRotation.Next(ESubAgentType.General, TwoModels, Only(TwoModels));
        string? third = SubAgentModelRotation.Next(ESubAgentType.General, TwoModels, Only(TwoModels));

        Assert.Equal(new[] { "model-a", "model-b", "model-a" }, new[] { first, second, third });
    }

    /// <summary>没配 = 跟随派活者/全局，不该白白推进游标</summary>
    [Fact]
    public void Next_NoCandidates_ReturnsNull()
    {
        Assert.Null(SubAgentModelRotation.Next(ESubAgentType.General, Array.Empty<string>(), Only(TwoModels)));
    }

    /// <summary>
    /// 钉不住的那一条跳过，而不是让整档回落：配了三个只起来一个，就该一直落在起来的那个上。
    /// 依次循环三趟都要有结果，不能因为轮到了坏的那条就交出 null。
    /// </summary>
    [Fact]
    public void Next_SkipsUnusableCandidates()
    {
        string[] candidates = { "dead-1", "alive", "dead-2" };

        string?[] picked =
        [
            SubAgentModelRotation.Next(ESubAgentType.General, candidates, Only("alive")),
            SubAgentModelRotation.Next(ESubAgentType.General, candidates, Only("alive")),
            SubAgentModelRotation.Next(ESubAgentType.General, candidates, Only("alive")),
        ];

        Assert.Equal(new[] { "alive", "alive", "alive" }, picked);
    }

    /// <summary>一条都钉不住：返回 null，由调用方回落派活者/全局模型</summary>
    [Fact]
    public void Next_AllUnusable_ReturnsNull()
    {
        Assert.Null(SubAgentModelRotation.Next(ESubAgentType.General, TwoModels, Only("alive")));
    }

    /// <summary>游标按档各一份：通用档推了两次，探索档还是从它自己的第一条起</summary>
    [Fact]
    public void Next_CursorIsPerSubAgentType()
    {
        SubAgentModelRotation.Next(ESubAgentType.General, TwoModels, Only(TwoModels));

        Assert.Equal("model-a", SubAgentModelRotation.Next(ESubAgentType.Explorer, TwoModels, Only(TwoModels)));
    }

    /// <summary>不是原样回吐候选名：判据做了规范化（如 ModelName 的 trim）时以判据为准</summary>
    [Fact]
    public void Next_ReturnsWhatThePredicateApproves()
    {
        string? picked = SubAgentModelRotation.Next(ESubAgentType.Brain, new[] { " spaced " },
            _ => "spaced");

        Assert.Equal("spaced", picked);
    }
}
