using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死图片总量预算：越线才裁、裁一次到底（回差）、只改发出去的那份、
/// 占位措辞不能沿用「这个模型读不了图」那一套。
///
/// 回差那几条是承重的：每裁一次都要从被改的那条往后丢掉整段前缀缓存，
/// 所以「多来一张就多裁一张」的行为与已停用的 <c>HistoryImageWindow</c> 没有区别。
///
/// 预算按 base64 后的传输字节算（撞网关的是请求体），所以这里的阈值也按同一口径造。
/// </summary>
public class ImageBudgetTests
{
    private const string ImageType = "image/png";

    /// <summary>一张 1MiB 原图在请求体里的长度</summary>
    private static readonly long OneImage = Wire(1024 * 1024);

    /// <summary>4 张触发、2 张停、张数不设限（让字节这一档先说话）</summary>
    private static readonly ImageBudgetLimits ByteLimited = new(OneImage * 4, OneImage * 2, 0);

    [Fact]
    public void UnderTrigger_ReturnsTheSameInstance()
    {
        List<ChatMessage> messages = [ImageMessage("两张", Mb(1), Mb(1))];

        Assert.Same(messages, ImageBudget.Apply(messages, ByteLimited));
    }

    [Fact]
    public void Off_NeverTouchesAnything()
    {
        List<ChatMessage> messages =
            [ImageMessage("五张", Mb(1), Mb(1), Mb(1), Mb(1), Mb(1))];

        Assert.Same(messages, ImageBudget.Apply(messages, ImageBudgetLimits.Off));
    }

    /// <summary>5 张越过 4 张的触发线：一次裁到 2 张,也就是丢最老的三个</summary>
    [Fact]
    public void OverTrigger_DropsTheOldestDownToTarget()
    {
        List<ChatMessage> messages =
        [
            ImageMessage("一", Mb(1)),
            ImageMessage("二", Mb(1)),
            ImageMessage("三", Mb(1)),
            ImageMessage("四", Mb(1)),
            ImageMessage("五", Mb(1)),
        ];

        IReadOnlyList<ChatMessage> sent = ImageBudget.Apply(messages, ByteLimited);

        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, sent.Select(ImagesIn));
        Assert.Contains("size budget", sent[0].Text);
        Assert.Equal("四", sent[3].Text); //没被动过的消息逐字不动
    }

    /// <summary>
    /// 回差：裁完之后再来一张图，只要没重新越过触发线就不再动刀。
    /// 否则每来一张新图前缀就断一次，与停用那条窗口没有区别。
    ///
    /// 输入必须是<b>裁过的那份</b>：<see cref="ImageBudget.Apply"/> 是纯函数，
    /// 拿原始历史再加一张，总量本来就又越线了，那测的不是回差
    /// </summary>
    [Fact]
    public void CutHoldsUntilTheTriggerIsPassedAgain()
    {
        List<ChatMessage> history =
        [
            ImageMessage("一", Mb(1)),
            ImageMessage("二", Mb(1)),
            ImageMessage("三", Mb(1)),
            ImageMessage("四", Mb(1)),
            ImageMessage("五", Mb(1)),
        ];
        IReadOnlyList<ChatMessage> trimmed = ImageBudget.Apply(history, ByteLimited);
        Assert.Equal(2, trimmed.Sum(ImagesIn)); //5 → 2

        //在裁过的那份上添一张:3 张仍在 4 张的触发线之下,一刀都不该动
        List<ChatMessage> grown = [.. trimmed, ImageMessage("六", Mb(1))];
        Assert.Same(grown, ImageBudget.Apply(grown, ByteLimited));
    }

    /// <summary>
    /// 一刀必须落到停止线之下，不能停在触发线上。
    /// 边走边裁的写法在这里会留下 4 张（正好 4MiB = 触发线），下一张进来就得再断一次前缀——
    /// 「每来一张断一次」那个毛病就回来了
    /// </summary>
    [Fact]
    public void OneCutLandsBelowTarget_EvenWhenTheTailCouldFillTheGap()
    {
        List<ChatMessage> ten =
        [
            .. Enumerable.Range(0, 10).Select(i => ImageMessage($"图{i}", Mb(1))),
        ];

        Assert.Equal(2, ImageBudget.Apply(ten, ByteLimited).Sum(ImagesIn));
    }

    /// <summary>同一份历史连算两次要得到同一刀（纯函数，不依赖消息 id / 时间戳）</summary>
    [Fact]
    public void SameHistoryTwice_ProducesTheSameCut()
    {
        List<ChatMessage> ten = [.. Enumerable.Range(0, 10).Select(i => ImageMessage($"图{i}", Mb(1)))];

        IReadOnlyList<ChatMessage> first = ImageBudget.Apply(ten, ByteLimited);
        IReadOnlyList<ChatMessage> second = ImageBudget.Apply(ten, ByteLimited);

        Assert.Equal(first.Select(ImagesIn), second.Select(ImagesIn));
        Assert.Equal(first.Select(m => m.Text), second.Select(m => m.Text));
        Assert.Same(first[^1], second[^1]); //没动过的消息复用同一实例
    }

    /// <summary>
    /// 旁路请求（写交接、群摘要）必须与主链用同一份预算。
    /// 漏了它，那一发照样是十几 MB；更糟的是从第一张被裁的图起就与主链分叉，
    /// 而从那里往后整段前缀缓存作废——交接那发恰恰是整个会话最贵的一次
    /// </summary>
    [Fact]
    public async Task SideRequest_TrimsWithTheSameBudget()
    {
        ImageBudgetLimits limits = new(OneImage * 4, OneImage * 2, 0);
        SideRequestRewriter side = HistoryRewriteChain.ForSideRequest(
            (transport, compaction) => HistoryRewriteChain.Build(transport, projectionPaths: null,
                attachImages: () => false, compaction, modelCannotSee: null, limits: () => limits),
            () => "deepseek-chat");
        List<ChatMessage> history = [.. Enumerable.Range(0, 10).Select(i => ImageMessage($"图{i}", Mb(1)))];

        IReadOnlyList<ChatMessage> rewritten = await side(history, 0, 0, CancellationToken.None);

        Assert.Equal(2, rewritten.Sum(ImagesIn));
    }

    /// <summary>
    /// 张数兜底也要有回差：越过上限一次腾到四分之三,而不是每来一张就多裁一张。
    /// 字节那条线远没到,所以这里只有张数在起作用
    /// </summary>
    [Fact]
    public void CountCap_TrimsToThreeQuarters_AndHoldsAfterwards()
    {
        ImageBudgetLimits capped = new(0, 0, 8);
        List<ChatMessage> nine = [.. Enumerable.Range(0, 9).Select(i => ImageMessage($"图{i}", 64))];
        IReadOnlyList<ChatMessage> trimmed = ImageBudget.Apply(nine, capped);
        Assert.Equal(6, trimmed.Sum(ImagesIn)); //9 → 6

        //在裁过的那份上添一张(7 张,仍在上限 8 之内):不动刀(回差就是 6→8 这段)
        List<ChatMessage> grown = [.. trimmed, ImageMessage("十", 64)];
        Assert.Same(grown, ImageBudget.Apply(grown, capped));
    }

    /// <summary>字节那条线关掉(0)不影响张数那条:两条是独立开关</summary>
    [Fact]
    public void ByteLineOff_DoesNotDisableTheCountCap()
    {
        ImageBudgetLimits onlyCount = new(0, 0, 4);
        List<ChatMessage> messages = [.. Enumerable.Range(0, 8).Select(i => ImageMessage($"图{i}", 64))];

        //只留到上限之内即可:裁到 3 之后又会涨回 4,再过线才再裁一次
        Assert.True(ImageBudget.Apply(messages, onlyCount).Sum(ImagesIn) <= 4);
    }

    /// <summary>
    /// 单张就超过停止线时也必须留一张：宁可这一发超标,也不能让模型以为本轮根本没图
    /// </summary>
    [Fact]
    public void NeverDropsEveryImage()
    {
        ImageBudgetLimits tiny = new(Mb(2), Mb(1), 0);
        List<ChatMessage> messages = [ImageMessage("一张巨图", Mb(8))];

        IReadOnlyList<ChatMessage> sent = ImageBudget.Apply(messages, tiny);

        Assert.Equal(1, sent.Sum(ImagesIn));
        Assert.Same(messages[0], sent[0]);
    }

    /// <summary>占位要并进原来那块文字里：部分 OpenAI 兼容网关遇到多个文本块会 400</summary>
    [Fact]
    public void NoteIsMergedIntoTheExistingTextBlock()
    {
        List<ChatMessage> messages = [ImageMessage("看看这两张", Mb(5), Mb(5))];

        ChatMessage sent = ImageBudget.Apply(messages, ByteLimited)[0];

        TextContent text = Assert.Single(sent.Contents.OfType<TextContent>());
        Assert.StartsWith("看看这两张", text.Text);
        Assert.Contains("size budget", text.Text);
        Assert.Equal(1, ImagesIn(sent)); //只裁掉最老的那张
    }

    /// <summary>
    /// 措辞不能与「模型看不了图」共用：那一套会让模型以为自己没有视觉能力，
    /// 于是不再看图、也不再去读——把还能用的手段白白丢掉
    /// </summary>
    [Fact]
    public void NoteDoesNotClaimTheModelIsBlind()
    {
        List<ChatMessage> messages = [ImageMessage("看看这两张", Mb(5), Mb(5))];

        string text = ImageBudget.Apply(messages, ByteLimited)[0].Text!;

        Assert.DoesNotContain("cannot read images", text);
        Assert.Contains("read the file again", text);
    }

    /// <summary>图在某条消息中间时只换最老的那张，前后内容与顺序都不许动</summary>
    [Fact]
    public void PartialMessage_KeepsTheSurvivingContent()
    {
        ChatMessage mixed = new(ChatRole.User,
        [
            new TextContent("三张一起看"),
            new DataContent(new byte[Mb(5)], ImageType),
            new DataContent(new byte[1024], ImageType),
            new DataContent(new byte[1024], ImageType),
        ]);

        ChatMessage sent = ImageBudget.Apply([mixed], ByteLimited)[0];

        Assert.Equal(2, sent.Contents.OfType<DataContent>().Count());
        Assert.StartsWith("三张一起看", sent.Text);
    }

    /// <summary>裁完不会动到原始消息：会话文件与界面渲染始终是全量</summary>
    [Fact]
    public void OriginalMessagesKeepTheirImages()
    {
        List<ChatMessage> messages = [ImageMessage("五张", Mb(1), Mb(1), Mb(1), Mb(1), Mb(1))];

        ImageBudget.Apply(messages, ByteLimited);

        Assert.Equal(5, messages.Sum(ImagesIn));
    }

    /// <summary>配置写反（target ≥ trigger）时不许在一发里把图丢干净</summary>
    [Fact]
    public void MisconfiguredTarget_StillKeepsSomething()
    {
        ImageBudgetLimits misconfigured = new(Mb(2), Mb(4), 0);
        List<ChatMessage> messages = [ImageMessage("五张", Mb(1), Mb(1), Mb(1), Mb(1), Mb(1))];

        Assert.True(ImageBudget.Apply(messages, misconfigured).Sum(ImagesIn) > 0);
    }

    private static int Mb(int count) => count * 1024 * 1024;

    /// <summary>原始字节 → base64 后的长度（预算口径就是它）</summary>
    private static long Wire(long raw) => (raw + 2) / 3 * 4;

    private static int ImagesIn(ChatMessage message) =>
        message.Contents.Count(c => c is DataContent data && data.HasTopLevelMediaType("image"));

    private static ChatMessage ImageMessage(string text, params long[] imageSizes)
    {
        List<AIContent> contents = [new TextContent(text)];
        foreach (long size in imageSizes) contents.Add(new DataContent(new byte[size], ImageType));
        return new ChatMessage(ChatRole.User, contents);
    }
}
