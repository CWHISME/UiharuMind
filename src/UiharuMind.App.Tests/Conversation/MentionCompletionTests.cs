using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 群里的 @ 补全（ADR 0049 决策 5）：认出光标前那半个名字、只换那一段、光标落在补进去的名字之后
/// </summary>
public class MentionCompletionTests
{
    private static readonly MentionTarget[] Members =
    [
        new("初春饰利", "风纪委员", null),
        new("一方通行", "第一位", null),
        new("白井黑子", "空间移动", null),
    ];

    [Theory]
    [InlineData("@", 1, 0, "")]
    [InlineData("@初", 2, 0, "初")]
    [InlineData("你好 @一方", 6, 3, "一方")]
    [InlineData("请＠白井 看看", 4, 1, "白井")] //全角 ＠
    [InlineData("你好@初", 4, 2, "初")] //紧跟汉字也算
    public void FindsTheMentionBeforeTheCaret(string text, int caret, int start, string query)
    {
        Assert.True(MentionCompletionSource.TryFindMention(text, caret, out CompletionMatch match));
        Assert.Equal(new CompletionMatch(start, caret, query), match);
    }

    [Theory]
    [InlineData("@初春饰利 你看", 8)] //已经写完、光标过了空格
    [InlineData("mail a@b.com", 12)] //邮箱
    [InlineData("没有点名", 4)]
    [InlineData("@初", 0)] //光标在 @ 前面
    public void IgnoresWhatIsNotAMention(string text, int caret)
    {
        Assert.False(MentionCompletionSource.TryFindMention(text, caret, out _));
    }

    [Fact]
    public async Task Candidates_PrefixFirstThenContains()
    {
        MentionCompletionSource source = new(() => Members);

        IReadOnlyList<CompletionCandidate> candidates = await source.GetCandidatesAsync("黑子");
        Assert.Equal(["@白井黑子"], candidates.Select(x => x.Label));

        candidates = await source.GetCandidatesAsync("");
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    public void NotAGroup_NeverMatches()
    {
        MentionCompletionSource source = new(() => []);

        Assert.False(source.TryMatch("@初", 2, out _));
    }

    [Fact]
    public async Task Accept_ReplacesOnlyTheHalfNameAndPutsTheCaretAfterIt()
    {
        string written = string.Empty;
        int writtenCaret = -1;
        int acceptedCaret = -1;
        CommandPaletteViewData palette = new((text, caret) =>
        {
            written = text;
            writtenCaret = caret;
        }, () => new CharacterData(), () => true, () => Members);
        palette.CandidateAccepted += caret => acceptedCaret = caret;

        await palette.RefreshAsync("你好 @一方后面的话", 6);
        Assert.True(palette.IsPickerOpen);
        Assert.True(palette.AcceptCandidate());

        Assert.Equal("你好 @一方通行 后面的话", written);
        Assert.Equal("你好 @一方通行 ".Length, writtenCaret);
        Assert.Equal(writtenCaret, acceptedCaret);
        Assert.False(palette.IsPickerOpen);
    }

    [Fact]
    public async Task Dismissed_StaysClosedUntilTheTextChanges()
    {
        CommandPaletteViewData palette = new((_, _) => { }, () => new CharacterData(), () => true, () => Members);
        await palette.RefreshAsync("@初", 2);
        palette.Dismiss();

        await palette.RefreshAsync("@初", 1); //只挪了光标
        Assert.False(palette.IsPickerOpen);

        await palette.RefreshAsync("@初春", 3);
        Assert.True(palette.IsPickerOpen);
    }

    /// <summary>
    /// 群壳的 / 补全全是死候选:发送路径在技能/命令解析之前就分流成群发言(见 SendCoreAsync 的群分支),
    /// 技能正文与 /compact 都不会被执行——补全就不该再弹它们,群里只留 @ 成员
    /// </summary>
    [Fact]
    public async Task GroupPalette_DoesNotOfferSlashCommands_ButKeepsMentions()
    {
        CommandPaletteViewData palette = new((_, _) => { }, () => new CharacterData(), () => true, () => Members);

        await palette.RefreshAsync("/comp", 5);
        Assert.False(palette.IsPickerOpen);

        await palette.RefreshAsync("/技能名 帮我看看", 7);
        Assert.False(palette.IsPickerOpen);

        await palette.RefreshAsync("@初", 2);
        Assert.True(palette.IsPickerOpen);
    }
}
