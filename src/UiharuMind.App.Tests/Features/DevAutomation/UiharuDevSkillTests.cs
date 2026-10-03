using UiharuMind.Features.DevAutomation;

namespace UiharuMind.App.Tests.Features.DevAutomation;

/// <summary>
/// 内置技能 uiharu-dev（ADR 0061）：步骤清单从各步骤自己的用法生成，不另写一份；只在开发者模式开着时可用
/// </summary>
public class UiharuDevSkillTests
{
    [Fact]
    public void EveryStep_HasAUsage()
    {
        IReadOnlyList<DevStepUsage> usages = new DevStepExecutor().Usages;

        Assert.Contains(usages, x => x.Op == "wait");
        Assert.All(usages, x => Assert.False(string.IsNullOrWhiteSpace(x.Usage), x.Op));
    }

    [Fact]
    public void Body_ListsEveryStepTheChannelAnswers()
    {
        string body = UiharuDevSkill.BuildBody();

        foreach (DevStepUsage usage in DevControlHost.HostUsages.Concat(new DevStepExecutor().Usages))
            Assert.Contains($"`{usage.Op}`：{usage.Usage}", body);
    }
}
