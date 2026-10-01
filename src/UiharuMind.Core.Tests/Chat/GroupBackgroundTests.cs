using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>从单聊开群带进去的背景草稿长什么样；交给模型写摘要的那段历史见 <c>HistorySupplyTests</c></summary>
public class GroupBackgroundTests
{
    [Fact]
    public void Compose_SaysWhereItCameFrom_AndLeavesRoomForTheQuestion()
    {
        string draft = GroupBackground.Compose("初春饰利", "  用户想定单聊拉人的规则。 ");

        Assert.StartsWith("（背景：这个群是从我和初春饰利的单聊开出来的", draft);
        Assert.Contains("\n\n用户想定单聊拉人的规则。\n\n", draft);
        Assert.EndsWith("\n\n", draft);
    }
}
