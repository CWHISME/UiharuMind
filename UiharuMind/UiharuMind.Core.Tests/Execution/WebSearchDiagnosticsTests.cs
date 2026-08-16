using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 健康面板的数据源。不断言"某个引擎是什么状态"——那取决于本机填没填 key、
/// 眼下有没有引擎在冷却,钉死它等于让测试跟着环境走。
/// </summary>
public class WebSearchDiagnosticsTests
{
    [Fact]
    public void Statuses_CoverWholeChain_InFallbackOrder()
    {
        IReadOnlyList<WebProviderStatus> statuses = WebSearchDiagnostics.GetStatuses();

        Assert.NotEmpty(statuses);
        Assert.Equal(Enumerable.Range(1, statuses.Count), statuses.Select(x => x.Order));
        Assert.All(statuses, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        Assert.Equal(statuses.Select(x => x.Name).Distinct().Count(), statuses.Count);
    }

    /// <summary>Firecrawl 无 key 可用,所以它永远不该显示成"未配置"</summary>
    [Fact]
    public void Firecrawl_IsNeverReportedAsUnconfigured()
    {
        WebProviderStatus? firecrawl = WebSearchDiagnostics.GetStatuses()
            .FirstOrDefault(x => x.Name == "Firecrawl");

        Assert.NotNull(firecrawl);
        Assert.NotEqual(EWebProviderState.NotConfigured, firecrawl.State);
    }

    /// <summary>冷却时长只在熔断态下才有意义,其余一律为零</summary>
    [Fact]
    public void Cooldown_IsZeroUnlessCooling()
    {
        Assert.All(WebSearchDiagnostics.GetStatuses(), s =>
        {
            if (s.State != EWebProviderState.Cooling) Assert.Equal(TimeSpan.Zero, s.Cooldown);
        });
    }
}
