using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 派活时给的身份截短后会署在回信上、当窗口标题：截出来要像个名字，不能切半个词、也不能看不出被截过
/// </summary>
public class SubAgentLabelClipTests
{
    [Theory]
    [InlineData("规格一致性审查员", "规格一致性审查员")]
    [InlineData("senior open-source docs/community maintainer", "senior open-source docs/community…")]
    [InlineData("concurrency and race-condition investigator for the scheduler", "concurrency and race-condition…")]
    [InlineData("资深 Avalonia 渲染/合成器调试工程师，专长全屏透明覆盖层与 Skia 的合成路径排查", "资深 Avalonia 渲染/合成器调试工程师，专长全屏透明覆盖层与 Skia…")]
    public void Clip_KeepsWholeWordsAndMarksTheCut(string role, string expected)
    {
        string clipped = SubAgentTool.Clip(role);

        Assert.Equal(expected, clipped);
        Assert.True(clipped.Length <= 40);
    }
}
