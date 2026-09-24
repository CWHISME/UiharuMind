using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

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
        return (new ConversationItemActions(items, new StubHost()), items);
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
    /// 配对幂等：已经认好来源的不碰，剩下的只在剩下的候选里找，重复调用不会漂移。
    /// </summary>
    [Fact]
    public void PairingLeavesAlreadyWiredThinkingCardsAlone()
    {
        var (actions, items) = Create();
        ChatMessage first = new(ChatRole.Assistant, [new TextReasoningContent(new string('一', 10))]);
        ChatMessage second = new(ChatRole.Assistant, [new TextReasoningContent(new string('二', 20))]);

        ThinkingItem settled = new();
        settled.Append(new string('s', 10));
        settled.SourceMessage = second;
        ThinkingItem fresh = new();
        fresh.Append(new string('f', 20));
        items.Add(settled);
        items.Add(fresh);

        actions.WireStreamed([first, second]);

        Assert.Same(second, settled.SourceMessage);
        Assert.Same(first, fresh.SourceMessage);
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
