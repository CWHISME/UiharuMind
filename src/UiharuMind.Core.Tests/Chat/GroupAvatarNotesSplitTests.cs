using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 化身投递末尾的私下交代（ADR 0055）：捎话/提醒/无限模式/当轮提示是说给化身的，
/// 呈现时剥成一段无发言人的旁白（回执样式），不并进最后一位发言人的气泡
/// </summary>
public class GroupAvatarNotesSplitTests
{
    private static readonly string[] Speakers = ["黑猫", "Alice", "Bob"];

    [Fact]
    public void TrailingNotes_BecomeOneNarrationSegment_SpeechIntact()
    {
        string input = "[Alice]: 第一版好了\n\n[Bob]: 我来改\n\n"
                       + GroupAvatarTranscript.KickoffNote("黑猫", "把登录页修了") + "\n\n"
                       + GroupAvatarTranscript.ReminderNote("黑猫", "这类改动可以替我拍") + "\n\n"
                       + GroupAvatarTranscript.InfiniteNote + "\n\n"
                       + GroupAvatarTranscript.SilentNote;

        IReadOnlyList<GroupDeliverySegment> segments = GroupTranscript.SplitDelivery(input, Speakers);

        Assert.Equal(3, segments.Count);
        Assert.Equal(new GroupDeliverySegment("Alice", "第一版好了"), segments[0]);
        Assert.Equal(new GroupDeliverySegment("Bob", "我来改"), segments[1]);
        Assert.Null(segments[2].Speaker);
        Assert.Contains("把登录页修了", segments[2].Body);
        Assert.Contains("这类改动可以替我拍", segments[2].Body);
        Assert.Contains("无限模式", segments[2].Body);
        Assert.Contains("没给出下一步", segments[2].Body);
    }

    [Fact]
    public void NothingNew_WithNotes_MergesIntoOneNarration()
    {
        string input = GroupAvatarTranscript.NothingNew + "\n\n" + GroupAvatarTranscript.InfiniteNote;

        IReadOnlyList<GroupDeliverySegment> segments = GroupTranscript.SplitDelivery(input, Speakers);

        GroupDeliverySegment only = Assert.Single(segments);
        Assert.Null(only.Speaker);
        Assert.Contains("没有新发言", only.Body);
        Assert.Contains("无限模式", only.Body);
    }

    [Fact]
    public void StoppedNote_WithoutKnowingTheUserName_IsStillCarved()
    {
        string input = "[Bob]: 嗯。\n\n" + GroupAvatarTranscript.StoppedNote("黑猫");

        IReadOnlyList<GroupDeliverySegment> segments = GroupTranscript.SplitDelivery(input, Speakers);

        Assert.Equal(2, segments.Count);
        Assert.Equal(new GroupDeliverySegment("Bob", "嗯。"), segments[0]);
        Assert.Null(segments[1].Speaker);
        Assert.Contains("按了停止", segments[1].Body);
    }

    [Fact]
    public void ReminderWithNewlinesAndBrackets_StripsTheWholeNote()
    {
        string input = "[Alice]: 好\n\n" + GroupAvatarTranscript.ReminderNote("黑猫", "第一行\n\n【A】第二行");

        IReadOnlyList<GroupDeliverySegment> segments = GroupTranscript.SplitDelivery(input, Speakers);

        Assert.Equal(2, segments.Count);
        Assert.Equal(new GroupDeliverySegment("Alice", "好"), segments[0]);
        Assert.Null(segments[1].Speaker);
        Assert.Contains("第一行\n\n【A】第二行", segments[1].Body);
    }

    [Fact]
    public void SpeechMentioningSimilarWords_IsUntouched()
    {
        // 只是正文里偶然提到类似的话：不在末尾、不成段，不剥
        string input = "[Alice]: 我觉得（你上次没给出下一步）这个说法挺有意思";

        Assert.Equal([new GroupDeliverySegment("Alice", "我觉得（你上次没给出下一步）这个说法挺有意思")],
            GroupTranscript.SplitDelivery(input, Speakers));
    }

    [Fact]
    public void OldParenthesisNotes_AreStillCarved()
    {
        // 已落盘的旧化身历史：交代还是（）写法，重开也要剥得掉
        static string Old(string note) => note.Replace('【', '（').Replace('】', '）');
        string input = "[Bob]: 嗯。\n\n" + Old(GroupAvatarTranscript.InfiniteNote) + "\n\n" +
                       Old(GroupAvatarTranscript.SilentNote);

        IReadOnlyList<GroupDeliverySegment> segments = GroupTranscript.SplitDelivery(input, Speakers);

        Assert.Equal(2, segments.Count);
        Assert.Equal(new GroupDeliverySegment("Bob", "嗯。"), segments[0]);
        Assert.Null(segments[1].Speaker);
    }

    [Fact]
    public void VoiceReminder_WithPrivateResumeNote_HidesBoth()
    {
        // 成员侧组装顺序：投递 + 重锚 + 私聊打断交代（无补位时以交代收尾，结尾是】）
        string input = "[Bob]: 嗯。\n\n" + GroupTranscript.VoiceReminder("你是Alice。") + "\n\n" +
                       GroupTranscript.PrivateResumeNote;

        Assert.Equal([new GroupDeliverySegment("Bob", "嗯。")],
            GroupTranscript.SplitDelivery(input, Speakers));
    }
}
