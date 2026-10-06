using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 化身投递锚点的文案（第三视角的输入侧）：四种状态各带什么指令、别把化身导向旧死循环
/// ——尤其 kickoff 时若本轮有新段必须先指新段，不能只让化身去读文件开头
/// </summary>
public class GroupAvatarDeliveryAnchorTests
{
    private const string Path = "$DRAFT/群流水.md";

    [Fact]
    public void HasNew_PointsAtSegmentOffset_AndIdentity()
    {
        string anchor = GroupAvatarTranscript.DeliveryAnchor(Path, kickoff: false, hasNew: true,
            firstIndex: 3, lastIndex: 4, startLine: 42, endLine: 61, totalLines: 100);

        Assert.Contains("新发言（群发言编号）#4-5", anchor);
        Assert.Contains($"先 {FileToolNames.Read}（offset=42）", anchor);
        Assert.Contains("用户（化身）", anchor);
        Assert.DoesNotContain("文件开头", anchor); //有新增时不把化身导向旧历史
    }

    [Fact]
    public void KickoffWithNew_PointsAtSegmentFirst_BackgroundOnlyAsAside()
    {
        string anchor = GroupAvatarTranscript.DeliveryAnchor(Path, kickoff: true, hasNew: true,
            firstIndex: 10, lastIndex: 11, startLine: 88, endLine: 100, totalLines: 120);

        Assert.Contains("新发言（群发言编号）#11-12", anchor);
        Assert.Contains("需要背景再 Read 文件开头", anchor); //背景只作补充
        Assert.Contains($"offset=88", anchor);
    }

    [Fact]
    public void KickoffWithoutNew_PointsHeadAndTail_AndWarnsAgainstOldSpeechAsUserLatest()
    {
        string anchor = GroupAvatarTranscript.DeliveryAnchor(Path, kickoff: true, hasNew: false,
            firstIndex: 0, lastIndex: 0, startLine: 0, endLine: 0, totalLines: 300);

        Assert.Contains("共 300 行", anchor);
        Assert.Contains($"offset=280", anchor); //末尾（300-20 兜底为 1）
        Assert.Contains("越靠后的段越新", anchor);
        Assert.Contains("别把旧发言当成「用户最新的话」", anchor);
        Assert.Contains(FileToolNames.Read, anchor);
    }

    [Fact]
    public void NoNew_BlocksIdlenessWithDirection()
    {
        string anchor = GroupAvatarTranscript.DeliveryAnchor(Path, kickoff: false, hasNew: false,
            firstIndex: 0, lastIndex: 0, startLine: 0, endLine: 0, totalLines: 300);

        Assert.Contains("没有新发言", anchor);
        Assert.Contains("没有新话不表示没事可做", anchor);
        Assert.Contains("要不要收尾，按你的规则办", anchor); //收尾只做指针，自指不指代不明的「卡片」
        Assert.Contains("用户（化身）", anchor);
    }
}