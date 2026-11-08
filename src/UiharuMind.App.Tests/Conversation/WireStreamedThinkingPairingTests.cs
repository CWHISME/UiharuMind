using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.App.Tests.TestDoubles;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 流式思考卡在轮末与历史配对。线上事故：模型以纯思考收尾（有推理、无正文、无工具调用），
/// 思考卡正在尾巴、回落无处可落，来源一直是空，对账把它当漏画又追加一张——
/// 同一段思考在界面上出现两次（字数相同、耗时不同：直播是墙钟，补画读存档统计）。
/// </summary>
public class WireStreamedThinkingPairingTests
{
    private sealed class StubHost : IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage? input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    private static (ConversationItemActions Actions, ObservableCollection<ConversationItemBase> Items) Create()
    {
        ObservableCollection<ConversationItemBase> items = new();
        return (new ConversationItemActions(items, new StubHost(), new RecordingMessageService()), items);
    }

    /// <summary>
    /// 事故原形：用户问 → 助手边想边调工具 → 工具结果 → 助手纯思考收尾。
    /// 工具结果不产新卡（只回写调用卡），尾巴上那张思考卡必须认到最后那条消息，
    /// 否则尾部检查报 2，对账追加出一张重复的思考卡。
    /// 正文气泡也不能偷走纯思考那条：它必须认真有正文的，不然两边交叉，
    /// 对账接着就报分歧要求全量重放。
    /// </summary>
    [Fact]
    public void ATrailingThinkingCardIsPairedWithTheReasoningOnlyMessage()
    {
        var (actions, items) = Create();
        ChatMessage question = new(ChatRole.User, "任务");
        ChatMessage call = new(ChatRole.Assistant,
        [
            new TextReasoningContent(new string('前', 8)),
            new TextContent("正文"),
            new FunctionCallContent("call-1", "Grep"),
        ]);
        ChatMessage result = new(ChatRole.Tool, [new FunctionResultContent("call-1", "结果")]);
        string tailReasoning = new('尾', 100);
        ChatMessage reasoningTail = new(ChatRole.Assistant, [new TextReasoningContent(tailReasoning)]);
        List<ChatMessage> history = [question, call, result, reasoningTail];

        TextConversationItem userBubble = new(true) { Message = "任务" };
        userBubble.SourceMessage = question;
        TextConversationItem replyBubble = new(false) { Message = "正文" };
        ToolCallItem toolCard = new() { CallId = "call-1", ToolName = "Grep" };
        ThinkingItem thinking = new();
        thinking.Append(tailReasoning);
        items.Add(userBubble);
        items.Add(replyBubble);
        items.Add(toolCard);
        items.Add(thinking);

        actions.WireStreamed(history);

        Assert.Same(call, replyBubble.SourceMessage);
        Assert.Same(call, toolCard.SourceMessage);
        Assert.Same(reasoningTail, thinking.SourceMessage);
        Assert.Null(ConversationOrderCheck.FindDivergence(items, history));
        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, history));
    }

    /// <summary>
    /// 长历史里同长度的思考不止一条时，尾巴上的流式卡必须认它上面那条之后的，不能认到更早的同长消息。
    /// 线上：444 条历史里 400 字的思考有两处（#214 与 #435），卡被认到 #214，真正那条于是仍算漏画，
    /// 补画又画一遍——同一段思考出现两次（耗时一个墙钟、一个读存档），对账随后报
    /// 「item #11 points at history #214, which is behind #433」。
    /// </summary>
    [Fact]
    public void ATailThinkingCardDoesNotClaimAnOlderMessageOfTheSameLength()
    {
        var (actions, items) = Create();
        // 很早就有一条 400 字的思考：长度上与尾巴那条一模一样，是陷阱
        ChatMessage old = new(ChatRole.Assistant, [new TextReasoningContent(new string('旧', 400))]);
        ChatMessage earlier = new(ChatRole.Assistant, [new FunctionCallContent("call-8", "Read")]);
        ChatMessage earlierResult = new(ChatRole.Tool, [new FunctionResultContent("call-8", "结果")]);
        string tailReasoning = new('新', 400);
        ChatMessage recent = new(ChatRole.Assistant,
        [
            new TextReasoningContent(tailReasoning),
            new FunctionCallContent("call-10", "Shell"),
        ]);
        List<ChatMessage> history = [old, earlier, earlierResult, recent];

        ToolCallItem earlierCard = new() { CallId = "call-8", ToolName = "Read" };
        ThinkingItem thinking = new();
        thinking.Append(tailReasoning);
        ToolCallItem tailCard = new() { CallId = "call-10", ToolName = "Shell" };
        items.Add(earlierCard);
        items.Add(thinking);
        items.Add(tailCard);

        actions.WireStreamed(history);

        Assert.Same(recent, thinking.SourceMessage);
        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, history));
        Assert.Null(ConversationOrderCheck.FindDivergence(items, history));
    }

    /// <summary>
    /// 长度对不上时（中途停止、截断）按顺序尽力配：配上总比空着强，
    /// 空着的思考卡删不掉（没有自己的删除按钮，来源也不在删除集合里）。
    /// </summary>
    [Fact]
    public void AThinkingCardWithMismatchedLengthFallsBackToOrder()
    {
        var (actions, items) = Create();
        ChatMessage first = new(ChatRole.Assistant, [new TextReasoningContent(new string('一', 10))]);
        ChatMessage second = new(ChatRole.Assistant, [new TextReasoningContent(new string('二', 20))]);

        ThinkingItem thinking = new();
        thinking.Append(new string('x', 7)); //谁都不是：只能按顺序认第一条
        items.Add(thinking);

        actions.WireStreamed([first, second]);

        Assert.Same(first, thinking.SourceMessage);
    }

    /// <summary>
    /// 思考与正文交替下发的模型：一条消息里有几段思考、界面上就有几张卡。
    /// 候选必须按<b>片段</b>长度比，否则按整条消息求和总是对不上，全部掉进顺序兜底，
    /// 一路错位——卡的耗时与字数随后被盖成前面几条消息的（实机：10 字的碎片顶着「7,901 字」）。
    ///
    /// 注：正文也拆成几段时，第二段的气泡仍只能认到同一轮里别的消息（一条消息只被
    /// 配对一次）——那是气泡侧的事，与这里的思考卡归属无关，不在此例。
    /// </summary>
    [Fact]
    public void InterleavedReasoningFragmentsPairWithTheirOwnMessage()
    {
        var (actions, items) = Create();
        // 前面一条已经认完的单段思考：错位配对会把后面的卡拖到它身上
        ChatMessage older = new(ChatRole.Assistant, [new TextReasoningContent(new string('旧', 906))]);
        ChatMessage olderResult = new(ChatRole.Tool, [new FunctionResultContent("call-1", "结果")]);
        // 交错的那一条：思考 251 → 正文 → 思考 10 → 工具调用
        string first = new('甲', 251);
        string second = new('乙', 10);
        ChatMessage interleaved = new(ChatRole.Assistant,
        [
            new TextReasoningContent(first),
            new TextContent("正文"),
            new TextReasoningContent(second),
            new FunctionCallContent("call-2", "Shell"),
        ]);
        List<ChatMessage> history = [older, olderResult, interleaved];

        ThinkingItem card1 = new();
        card1.Append(first);
        TextConversationItem bubble = new(false) { Message = "正文" };
        ThinkingItem card2 = new();
        card2.Append(second);
        ToolCallItem toolCard = new() { CallId = "call-2", ToolName = "Shell" };
        items.Add(card1);
        items.Add(bubble);
        items.Add(card2);
        items.Add(toolCard);

        actions.WireStreamed(history);

        Assert.Same(interleaved, card1.SourceMessage);
        Assert.Same(interleaved, bubble.SourceMessage);
        Assert.Same(interleaved, card2.SourceMessage);
        Assert.Same(interleaved, toolCard.SourceMessage);
        Assert.Null(ConversationOrderCheck.FindDivergence(items, history));
        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, history));
    }

    /// <summary>
    /// 交错消息里只剩一张卡（另一段被裁掉/没画）：这张卡仍然只能认它自己那一条，
    /// 不能因为长度碰不上就去认前面的消息——那是把它变成了另一条消息的卡。
    /// </summary>
    [Fact]
    public void ASingleFragmentOfAnInterleavedMessageStaysOnIt()
    {
        var (actions, items) = Create();
        ChatMessage older = new(ChatRole.Assistant, [new TextReasoningContent(new string('旧', 300))]);
        string first = new('甲', 251);
        string second = " filtered.";
        ChatMessage interleaved = new(ChatRole.Assistant,
        [
            new TextReasoningContent(first),
            new TextContent("正文"),
            new TextReasoningContent(second),
        ]);

        ThinkingItem card = new();
        card.Append(second.TrimStart()); //卡里的段首空白已被剥掉（见 ConversationTranscript.AppendThinking）
        TextConversationItem bubble = new(false) { Message = "正文" };
        items.Add(card);
        items.Add(bubble);

        actions.WireStreamed([older, interleaved]);

        Assert.Same(interleaved, card.SourceMessage);
        Assert.Same(interleaved, bubble.SourceMessage);
    }

    /// <summary>
    /// 跨调用重配：一条交错消息的第一段卡已经认好、第二段卡到下一次落盘才建出来。
    /// 已认走的只是<b>那一段</b>，不是整条消息——第二段仍得按长度认回来，
    /// 否则它会被顺序兜底拽到前面别的消息上（之后统计又照着那条消息盖下去）。
    /// </summary>
    [Fact]
    public void SecondFragmentPairsEvenWhenTheMessageIsAlreadyClaimed()
    {
        var (actions, items) = Create();
        ChatMessage older = new(ChatRole.Assistant, [new TextReasoningContent(new string('旧', 400))]);
        string first = new('甲', 251);
        string second = new('乙', 10);
        ChatMessage interleaved = new(ChatRole.Assistant,
        [
            new TextReasoningContent(first),
            new TextContent("正文"),
            new TextReasoningContent(second),
        ]);

        ThinkingItem card1 = new();
        card1.Append(first);
        card1.SourceMessage = interleaved; //上一次重配时认好的
        ThinkingItem card2 = new();
        card2.Append(second); //这一次才建出来
        items.Add(card1);
        items.Add(card2);

        actions.WireStreamed([older, interleaved]);

        Assert.Same(interleaved, card1.SourceMessage);
        Assert.Same(interleaved, card2.SourceMessage);
    }

    /// <summary>
    /// 已知代价：一张还在流、长度尚未长齐的卡，长度扣不掉存档里那一段，于是那一段仍留在候选里，
    /// 后到的「长度正好对上」的卡会再认一次（一段两卡）。
    /// 取舍：让<b>齐的赢</b>——碎片占着位置反而会把真正那条挤去前面别的消息（旧行为）。
    /// （想把碎片也钉住，得给「卡是否还在长」一个真实标记：现在 `IsDone` 默认就是 true，
    /// 无法区分流中与收尾，所以这一条暂时只能靠长度。）
    /// </summary>
    [Fact]
    public void AStillGrowingCard_CanLoseItsSegmentToTheCompleteOne()
    {
        var (actions, items) = Create();
        ChatMessage older = new(ChatRole.Assistant, [new TextReasoningContent(new string('旧', 400))]);
        string segment = new('段', 200);
        ChatMessage message = new(ChatRole.Assistant,
            [new TextReasoningContent(segment), new FunctionCallContent("c1", "Shell")]);

        ThinkingItem growing = new();
        growing.Append(new string('半', 30)); //还在流：只攒到 30，与存档的 200 对不上
        growing.SourceMessage = message;
        ThinkingItem late = new();
        late.Append(segment);
        items.Add(growing);
        items.Add(late);

        actions.WireStreamed([older, message]);

        Assert.Same(message, growing.SourceMessage);
        Assert.Same(message, late.SourceMessage); //齐的那张赢
    }

    /// <summary>
    /// 配对幂等：已经认好来源的不碰。
    ///
    /// 而「已认走」只占掉<b>它那一段</b>：上面那张卡长度是 10，目标消息只有一段 20，扣不掉——
    /// 说明它不是这条消息的段（截断/取消的半截卡）。这种卡不得把整段名额占掉，
    /// 否则真正的 20 字卡只能去认前面别的消息（旧行为，删除与统计都会跟着错）。
    /// </summary>
    [Fact]
    public void PairingLeavesAlreadyWiredThinkingCardsAlone()
    {
        var (actions, items) = Create();
        ChatMessage first = new(ChatRole.Assistant, [new TextReasoningContent(new string('一', 10))]);
        ChatMessage second = new(ChatRole.Assistant, [new TextReasoningContent(new string('二', 20))]);

        ThinkingItem settled = new();
        settled.Append(new string('s', 10));
        settled.SourceMessage = second; // 人工造成的不一致：长度对不上
        ThinkingItem fresh = new();
        fresh.Append(new string('f', 20));
        items.Add(settled);
        items.Add(fresh);

        actions.WireStreamed([first, second]);

        Assert.Same(second, settled.SourceMessage);
        Assert.Same(second, fresh.SourceMessage);
    }

    /// <summary>
    /// 同长多卡按显示顺序配对：长度没有区分度时顺序就是唯一的判据，
    /// 两张卡各认各的，不许交换——交换在界面上看不出，但删除会带错轮次。
    /// </summary>
    [Fact]
    public void SameLengthCardsPairInDisplayOrder()
    {
        var (actions, items) = Create();
        ChatMessage first = new(ChatRole.Assistant, [new TextReasoningContent(new string('一', 50))]);
        ChatMessage second = new(ChatRole.Assistant, [new TextReasoningContent(new string('二', 50))]);
        List<ChatMessage> history = [first, second];

        ThinkingItem head = new();
        head.Append(new string('a', 50));
        ThinkingItem tail = new();
        tail.Append(new string('b', 50));
        items.Add(head);
        items.Add(tail);

        actions.WireStreamed(history);

        Assert.Same(first, head.SourceMessage);
        Assert.Same(second, tail.SourceMessage);
        Assert.Null(ConversationOrderCheck.FindDivergence(items, history));
    }

    /// <summary>
    /// 长度与顺序打架时顺序优先：前卡长度恰好等于后一条消息、后卡等于前一条时，
    /// 按长度会交叉（A→后、B→前），删除时 A 轮的卡跟着 B 轮的消息走，
    /// 对账还会报出假分歧。不交叉的底线是单调：后卡不许认到前卡之前。
    /// 实在没位置的后卡跟前卡抱团——同一条消息本来就会拆出多张卡。
    /// </summary>
    [Fact]
    public void CrossedLengthsNeverCrossPairing()
    {
        var (actions, items) = Create();
        ChatMessage first = new(ChatRole.Assistant, [new TextReasoningContent(new string('一', 200))]);
        ChatMessage second = new(ChatRole.Assistant, [new TextReasoningContent(new string('二', 100))]);
        List<ChatMessage> history = [first, second];

        ThinkingItem head = new();
        head.Append(new string('a', 100)); //长度上是后一条的
        ThinkingItem tail = new();
        tail.Append(new string('b', 200)); //长度上是前一条的
        items.Add(head);
        items.Add(tail);

        actions.WireStreamed(history);

        Assert.Same(second, head.SourceMessage);
        Assert.NotNull(tail.SourceMessage);
        Assert.True(history.IndexOf(head.SourceMessage!) <= history.IndexOf(tail.SourceMessage!));
        Assert.Null(ConversationOrderCheck.FindDivergence(items, history));
        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, history));
    }

    /// <summary>
    /// 取消打断的半截思考没进历史：历史里没有可认的推理消息时沿用原来的回落——
    /// 隔壁条目的来源，至少保证卡片能跟着删，而不是留下删不掉的残留。
    /// </summary>
    [Fact]
    public void ACancelledThinkingWithoutHistoryFallsBackToNeighbor()
    {
        var (actions, items) = Create();
        ChatMessage question = new(ChatRole.User, "任务");
        ChatMessage answer = new(ChatRole.Assistant, "正文"); //没有推理内容：思考卡无处可认
        List<ChatMessage> history = [question, answer];

        TextConversationItem userBubble = new(true) { Message = "任务" };
        userBubble.SourceMessage = question;
        ThinkingItem cancelled = new();
        cancelled.Append(new string('x', 30)); //被打断的半截，历史里没有它
        TextConversationItem replyBubble = new(false) { Message = "正文" };
        items.Add(userBubble);
        items.Add(cancelled);
        items.Add(replyBubble);

        actions.WireStreamed(history);

        Assert.Same(answer, replyBubble.SourceMessage);
        Assert.Same(answer, cancelled.SourceMessage);
    }

    /// <summary>
    /// 上一个用例的边界：半截思考正在尾巴、后面没有条目可回落时仍是空。
    /// 接受为已知下限——老代码同样是空，没有退化；对账本来就跳过空来源，
    /// 尾部已被正文盖住时也不会误报追加。
    /// </summary>
    [Fact]
    public void ATrailingCancelledThinkingWithNoNeighborStaysUnpaired()
    {
        var (actions, items) = Create();
        ChatMessage question = new(ChatRole.User, "任务");
        ChatMessage answer = new(ChatRole.Assistant, "正文");
        List<ChatMessage> history = [question, answer];

        TextConversationItem userBubble = new(true) { Message = "任务" };
        userBubble.SourceMessage = question;
        TextConversationItem replyBubble = new(false) { Message = "正文" };
        ThinkingItem cancelled = new();
        cancelled.Append(new string('x', 30));
        items.Add(userBubble);
        items.Add(replyBubble);
        items.Add(cancelled);

        actions.WireStreamed(history);

        Assert.Null(cancelled.SourceMessage);
        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, history));
    }
}
