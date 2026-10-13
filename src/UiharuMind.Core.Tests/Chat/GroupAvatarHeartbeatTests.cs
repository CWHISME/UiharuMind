using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 无限模式心跳状态行（ADR 0055 §8）：整条以 ⟦…⟧ 包裹 = 状态行，由程序拦下、不进群。
/// 先只认整条，混在正文中间的心跳片段不剥（免得误吞正文）
/// </summary>
public class GroupAvatarHeartbeatTests
{
    [Theory]
    [InlineData("⟦运行中｜锚点：x｜本轮 delta：有/无｜未决问题：0→1｜本轮手段：指派⟧", true)]
    [InlineData("⟦ 状态行 ⟧", true)]
    [InlineData("我建议 @Alice 做 X", false)]
    [InlineData("⟦状态⟧ 接着做正文", false)] //混在正文里：暂不剥
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("⟦只开不闭", false)]
    public void IsHeartbeatBody_OnlyWholeLineWrappedInHeartbeatBrackets(string text, bool expected)
    {
        Assert.Equal(expected, GroupAvatarTranscript.IsHeartbeatBody(text));
    }
}