using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 无限模式心跳状态行（ADR 0055 §8）：整行以 ⟦…⟧ 包裹 = 状态行，由程序剥掉、不进群。
/// 只认「整行」，正文行（哪怕含半截 ⟦ 符号）不动，避免误吞
/// </summary>
public class GroupAvatarHeartbeatTests
{
    [Theory]
    [InlineData("⟦运行中｜锚点：x｜本轮 delta：有/无⟧", "")]
    [InlineData("⟦ 状态行 ⟧", "")]
    [InlineData("⟦运行中｜锚点：x⟧\n我们继续做 @Alice", "我们继续做 @Alice")]
    [InlineData("先总结一下：……\n⟦运行中｜delta：有⟧\n@Alice 去做", "先总结一下：……\n@Alice 去做")]
    [InlineData("我建议 @Alice 做 X", "我建议 @Alice 做 X")]
    [InlineData("⟦状态⟧ 接着做正文", "⟦状态⟧ 接着做正文")] //半截符号在正文行里：不动
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("⟦只开不闭", "⟦只开不闭")]
    public void WithoutHeartbeatLines_StripsOnlyWholeHeartbeatLines(string text, string expected)
    {
        Assert.Equal(expected, GroupAvatarTranscript.WithoutHeartbeatLines(text));
    }

    [Theory]
    [InlineData("⟦运行中｜锚点：x⟧\n我们继续做 @Alice", "⟦运行中｜锚点：x⟧")]
    [InlineData("a\n⟦h1⟧\nb\n⟦h2⟧", "⟦h1⟧\n⟦h2⟧")]
    [InlineData("没有心跳行", "")]
    [InlineData("⟦状态⟧ 正文行不认", "")]
    public void HeartbeatLinesOf_ExtractsOnlyWholeHeartbeatLinesInOrder(string text, string expectedJoined)
    {
        Assert.Equal(expectedJoined, string.Join("\n", GroupAvatarTranscript.HeartbeatLinesOf(text)));
    }
}