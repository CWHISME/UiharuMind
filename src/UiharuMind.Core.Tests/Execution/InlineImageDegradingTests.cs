using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死「模型看不了图时，历史里内联的图片不再逐轮重发」。
///
/// 承重之处有三条：
/// <list type="number">
/// <item>不得原地改历史消息——会话文件是权威来源，这里一改到原对象，磁盘上那张图就没了；
/// 换回视觉模型就该原样可用。</item>
/// <item>判据必须<b>每次请求现读</b>。固化在装配期会两头错：普通对话形态的装配快照里不含视觉一项
/// （换模型不重建），于是「视觉→盲」该降的不降、「盲→视觉」反而把刚发的图也抹掉。</item>
/// <item>降级挂在压缩外面：压缩的预算按真实要发的字节算，图先摘掉它才不会提前动手。</item>
/// </list>
///
/// 识图投影那一层在降级<b>里面</b>，它现造的字节不经过降级，所以它的附图开关也必须现问
/// （<see cref="ReadPreviews_FollowTheLiveJudgmentToo"/> 钉住）。
/// </summary>
public class InlineImageDegradingTests
{
    private static ChatMessage WithImage(string text, int images = 1, int bytesPerImage = 3) => new(ChatRole.User,
    [
        .. Enumerable.Range(0, images)
            .Select(_ => new DataContent(new byte[bytesPerImage].Select((_, i) => (byte)i).ToArray(), "image/png")),
        new TextContent(text),
    ]);

    [Fact]
    public void ImageIsReplacedByAPlaceholder_TheTextStays()
    {
        ChatMessage degraded = Assert.Single(InlineImageDegrading.Project([WithImage("看这张图")]));

        Assert.Empty(degraded.Contents.OfType<DataContent>());
        string text = Assert.Single(degraded.Contents.OfType<TextContent>()).Text;
        Assert.StartsWith("看这张图", text, StringComparison.Ordinal);
        Assert.Contains("Image omitted", text, StringComparison.Ordinal);
    }

    /// <summary>占位并进原来那块文字里：多一个文本块会让部分兼容网关 400</summary>
    [Fact]
    public void PlaceholderIsMergedIntoTheSingleTextBlock()
    {
        ChatMessage degraded = Assert.Single(InlineImageDegrading.Project([WithImage("说明")]));

        Assert.Single(degraded.Contents.OfType<TextContent>());
    }

    /// <summary>用户只贴了张截图、一个字没打：占位自己成一块文本，消息不会变空</summary>
    [Fact]
    public void ImageOnlyMessage_GetsAPlaceholderOfItsOwn()
    {
        ChatMessage onlyImage = new(ChatRole.User, [new DataContent(new byte[] { 1 }, "image/png")]);

        ChatMessage degraded = Assert.Single(InlineImageDegrading.Project([onlyImage]));

        TextContent note = Assert.IsType<TextContent>(Assert.Single(degraded.Contents));
        Assert.Contains("Image omitted", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralImages_CollapseToASingleNoteThatNamesTheCount()
    {
        ChatMessage degraded = Assert.Single(InlineImageDegrading.Project([WithImage("三张", 3)]));

        string text = Assert.Single(degraded.Contents.OfType<TextContent>()).Text;
        Assert.Contains("3 images omitted", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NonImageData_IsLeftAlone()
    {
        ChatMessage withPdf = new(ChatRole.User,
            [new DataContent(new byte[] { 1 }, "application/pdf"), new TextContent("文件")]);

        Assert.Same(withPdf, Assert.Single(InlineImageDegrading.Project([withPdf])));
    }

    /// <summary>没有图的会话一个对象都不多造（这层每发一次都过，别白复制整份历史）</summary>
    [Fact]
    public void NoImages_ReturnsTheSameInstance()
    {
        List<ChatMessage> messages = [new(ChatRole.User, "纯文本"), new(ChatRole.Assistant, "回复")];

        Assert.Same(messages, InlineImageDegrading.Project(messages));
    }

    /// <summary>历史原对象不动，时间戳与消息标识带过去（落盘与气泡排序都要它们）</summary>
    [Fact]
    public void OriginalHistory_IsNeverMutated_AndTheCopyKeepsItsIdentity()
    {
        ChatMessage original = WithImage("看这张图");
        original.MessageId = "m1";
        original.CreatedAt = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        ChatMessage degraded = Assert.Single(InlineImageDegrading.Project([original]));

        Assert.Single(original.Contents.OfType<DataContent>()); //会话文件里的图片必须还在
        Assert.Equal(2, original.Contents.Count);
        Assert.Equal("m1", degraded.MessageId);
        Assert.Equal(original.CreatedAt, degraded.CreatedAt);
    }

    [Fact]
    public void CountIn_CountsImagesNotMessages()
    {
        List<ChatMessage> messages =
            [WithImage("a", 2), new(ChatRole.Assistant, "回复"), WithImage("b"), new(ChatRole.User, "纯文本")];

        Assert.Equal(3, InlineImageDegrading.CountIn(messages));
        Assert.Equal(0, InlineImageDegrading.CountIn(null));
        Assert.Equal(0, InlineImageDegrading.CountIn([]));
    }

    /// <summary>
    /// 判据每发一次现问：同一个句柄里换模型（不重建 agent）也必须当场跟着翻，
    /// 两个方向都要——该降的不降会 400，不该降的降了会把用户刚发的图也抹掉。
    /// </summary>
    [Fact]
    public async Task Chain_FollowsTheLiveJudgment_AcrossAModelSwitch()
    {
        bool blind = true;
        MessageCapturingChatClient capture = new();
        IChatClient chain = HistoryRewriteChain.Build(capture, projectionPaths: null, attachImages: () => false,
            compaction: null, modelCannotSee: () => blind);

        await SendAsync(chain, [WithImage("看这张图")]);
        Assert.Empty(capture.Captured[^1].Contents.OfType<DataContent>()); //盲：摘掉

        blind = false;
        await SendAsync(chain, [WithImage("看这张图")]);
        Assert.Single(capture.Captured[^1].Contents.OfType<DataContent>()); //看得见：原样带图
    }

    /// <summary>
    /// 装配那条路读的是<b>会发这一发的那个模型</b>，不是装配时定下的那个值：
    /// 把判据换回 <c>plan.ModelSupportsVision</c>（层照样挂着，但读死值），这条就红。
    /// </summary>
    [Theory]
    [InlineData(true)] //装配时以为看得见
    [InlineData(false)] //装配时以为看不见
    public async Task Chain_FromPlan_IgnoresTheFrozenVisionFlag(bool planSaysVision)
    {
        ModelRunningData? live = Model(seesImages: false); //此刻是盲模型
        AgentAssemblyPlan plan = Plan(planSaysVision, () => live);
        MessageCapturingChatClient capture = new();
        IChatClient chain = HistoryRewriteChain.Build(capture, plan, null);

        await SendAsync(chain, [WithImage("看这张图")]);

        Assert.Empty(capture.Captured[^1].Contents.OfType<DataContent>());
        Assert.Contains("Image omitted", capture.Captured[^1].Text, StringComparison.Ordinal);
    }

    /// <summary>方向反过来也要跟着翻：计划里定的是「看不见」，此刻换成了视觉模型</summary>
    [Fact]
    public async Task Chain_FromPlan_FollowsTheModelSwitchAtRequestTime()
    {
        ModelRunningData? live = Model(seesImages: true);
        AgentAssemblyPlan plan = Plan(modelSeesImages: false, () => live);
        MessageCapturingChatClient capture = new();
        IChatClient chain = HistoryRewriteChain.Build(capture, plan, null);

        await SendAsync(chain, [WithImage("看这张图")]);
        Assert.Single(capture.Captured[^1].Contents.OfType<DataContent>()); //看得见：带图

        live = Model(seesImages: false); //同一个句柄换模型，不重建
        await SendAsync(chain, [WithImage("看这张图")]);
        Assert.Empty(capture.Captured[^1].Contents.OfType<DataContent>());
    }

    /// <summary>
    /// 识图投影在降级<b>里面</b>，它现造的字节不经过降级，所以它的附图开关同样要现问：
    /// 装配时以为看得见图（历史里留着 Read 看过的预览）、此刻其实是盲模型，那一张也必须换成占位。
    /// </summary>
    [Fact]
    public async Task ReadPreviews_FollowTheLiveJudgmentToo()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-degrade-" + Guid.NewGuid().ToString("N"));
        try
        {
            string draft = Path.Combine(root, "draft");
            string preview = Path.Combine(draft, "images", "previews", "shot.png");
            Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
            await File.WriteAllBytesAsync(preview, TestImages.Png(4, 4),
                TestContext.Current.CancellationToken);

            bool vision = false;
            MessageCapturingChatClient capture = new();
            IChatClient chain = HistoryRewriteChain.Build(capture,
                new AgentPathResolver(Path.Combine(root, "ws"), draft, string.Empty),
                attachImages: () => vision, compaction: null, modelCannotSee: () => !vision);

            await SendAsync(chain, ViewedImage("shot.png", preview));
            Assert.Empty(capture.Captured[^1].Contents.OfType<DataContent>()); //盲：投影也只发占位
            Assert.Contains("Image omitted", capture.Captured[^1].Text, StringComparison.Ordinal);

            vision = true;
            await SendAsync(chain, ViewedImage("shot.png", preview));
            Assert.Single(capture.Captured[^1].Contents.OfType<DataContent>()); //看得见：照旧附图
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 装配那条路上，投影的附图开关同样是现问的：计划里定的是「看得见」（历史里留着 Read 看过的预览），
    /// 而此刻真正发请求的模型是盲的——那一张也必须换成占位。
    /// 换回 <c>plan.ReadAttachesImages</c>（装配期 bool）这条就红。
    /// </summary>
    [Fact]
    public async Task ReadPreviews_FromPlan_FollowTheLiveJudgment()
    {
        string root = Path.Combine(Path.GetTempPath(), "uiharu-degrade-" + Guid.NewGuid().ToString("N"));
        try
        {
            string draft = Path.Combine(root, "draft");
            string preview = Path.Combine(draft, "images", "previews", "shot.png");
            Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
            await File.WriteAllBytesAsync(preview, TestImages.Png(4, 4),
                TestContext.Current.CancellationToken);

            ModelRunningData live = Model(seesImages: false); //此刻是盲模型
            AgentAssemblyPlan plan = new()
            {
                Profile = new AgentBuildProfile
                {
                    Character = new CharacterData
                    {
                        CharacterId = "a", IsAgent = true,
                        Tools = new AgentToolConfig { EnableVisionTool = true },
                    },
                    SessionModelSource = () => live,
                },
                OutputRoomDirectory = draft,
                ModelSupportsVision = true, //装配时以为看得见：正是漂移那一侧
            };
            MessageCapturingChatClient capture = new();
            IChatClient chain = HistoryRewriteChain.Build(capture, plan, null);

            await SendAsync(chain, ViewedImage("shot.png", preview));
            Assert.Empty(capture.Captured[^1].Contents.OfType<DataContent>());
            Assert.Contains("Image omitted", capture.Captured[^1].Text, StringComparison.Ordinal);

            live = Model(seesImages: true);
            await SendAsync(chain, ViewedImage("shot.png", preview));
            Assert.Single(capture.Captured[^1].Contents.OfType<DataContent>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 降级在压缩外面：压缩读到的原始历史里已经没有那些字节。把它挪进压缩里面，
    /// 盲模型就会按虚高的量提前压缩（这条断言会红）。
    /// </summary>
    [Fact]
    public async Task CompactionSeesTheDegradedHistory_NotTheImages()
    {
        long blind = await RawHistoryAsync(modelCannotSee: true);
        long seeing = await RawHistoryAsync(modelCannotSee: false);

        Assert.True(blind > 0, "压缩该判过一次");
        Assert.True(blind < seeing, $"压缩该看到降级后的字节：盲 {blind}，看得见 {seeing}");
    }

    /// <summary>压缩判过之后记下的原始历史大小（借一份大到不会触发的策略）</summary>
    private static async Task<long> RawHistoryAsync(bool modelCannotSee)
    {
        TurnInputEstimate estimate = new();
        IChatClient chain = HistoryRewriteChain.Build(new MessageCapturingChatClient(), projectionPaths: null,
            attachImages: () => false, compaction: HistoryCompaction.Create(() => 100_000_000, estimate),
            modelCannotSee: () => modelCannotSee);

        List<ChatMessage> history = [WithImage("看这张图", bytesPerImage: 200_000)];
        HistoryCompactionTests.AddToolGroup(history, 0); //折叠策略要有工具组才动手判
        await SendAsync(chain, history);
        return estimate.LastRawHistory;
    }

    /// <summary>一条「Read 看过图」的历史：助手发起调用，工具结果里有预览路径</summary>
    private static List<ChatMessage> ViewedImage(string label, string preview) =>
    [
        new(ChatRole.Assistant,
        [
            new FunctionCallContent("c1", FileToolNames.Read,
                new Dictionary<string, object?> { ["filePath"] = label }),
        ]),
        new(ChatRole.Tool,
            [new FunctionResultContent("c1", $"Attached: {label}\nPreview: {preview}\nPreview size: 4x4")]),
    ];

    private static async Task SendAsync(IChatClient chain, IReadOnlyList<ChatMessage> messages)
    {
        await foreach (ChatResponseUpdate _ in chain.GetStreamingResponseAsync(messages,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
        }
    }

    /// <summary>普通对话形态的计划：装配链时只用到形态与「钉选模型从哪儿来」，不碰单例</summary>
    private static AgentAssemblyPlan Plan(bool modelSeesImages, Func<ModelRunningData?> pinned) => new()
    {
        Profile = new AgentBuildProfile
        {
            Character = new CharacterData { CharacterId = "roleplay", IsAgent = false },
            SessionModelSource = pinned,
        },
        ModelSupportsVision = modelSeesImages,
    };

    /// <summary>
    /// 假模型一律用<b>远程</b>型号：<see cref="ServingVision"/> 内部走 <c>LazyChatClient.ServingModel</c>，
    /// 而远程候选在那里直接 UseCandidate、不会去比全局模型。用本地 GGUF 的话，一旦哪个测试
    /// 调了 <c>ReloadModelList</c> 把开发者收藏填进去，这几条的答案就会随机器变。
    /// </summary>
    private static ModelRunningData Model(bool seesImages) => new(new RemoteModelInfo
    {
        Config = new RemoteModelConfig { ModelName = "m", IsVision = seesImages },
    });
}
