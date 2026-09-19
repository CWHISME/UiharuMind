using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 界面与历史的对账。一轮跑着的时候条目来自两股（实时内容流、历史落盘），
/// 插话还要先乐观显示后落盘——顺序靠推演已经错过两次，所以留这道事后校验：
/// 错了能自己发现，由调用方重放纠正。
/// </summary>
public class ConversationOrderCheckTests
{
    private static TextConversationItem Item(ChatMessage? source, bool isUser = false)
    {
        TextConversationItem item = new(isUser) { Message = source?.Text ?? string.Empty };
        item.SourceMessage = source;
        return item;
    }

    [Fact]
    public void TheUsualCaseIsInOrder()
    {
        ChatMessage first = new(ChatRole.User, "任务");
        ChatMessage second = new(ChatRole.Assistant, "回答");
        List<ConversationItemBase> items = [Item(first, true), Item(second)];

        Assert.Null(ConversationOrderCheck.FindDivergence(items, [first, second]));
    }

    /// <summary>"回答排在提问前面"就是这个形状</summary>
    [Fact]
    public void AnItemPointingBackwardsIsDivergence()
    {
        ChatMessage question = new(ChatRole.User, "555");
        ChatMessage answer = new(ChatRole.Assistant, "回答");
        List<ConversationItemBase> items = [Item(answer), Item(question, true)];

        Assert.NotNull(ConversationOrderCheck.FindDivergence(items, [question, answer]));
    }

    /// <summary>
    /// 还没落盘的气泡（刚插的那句）没有来源消息。把它当成缺失会在每轮结尾误判，
    /// 于是每轮都重放一次——闪给用户看。
    /// </summary>
    [Fact]
    public void AnUnpairedBubbleIsNotDivergence()
    {
        ChatMessage first = new(ChatRole.User, "任务");
        List<ConversationItemBase> items = [Item(first, true), Item(null, true)];

        Assert.Null(ConversationOrderCheck.FindDivergence(items, [first]));
    }

    /// <summary>
    /// 来源已不在历史里就是分歧：跑着时被原地替换掉的旧报告正是这个形状。
    /// 跳过它的话，替换不改变历史条数，尾部检查也会报 0——旧行永远修不好。
    /// </summary>
    [Fact]
    public void ASourceMissingFromHistoryIsDivergence()
    {
        ChatMessage question = new(ChatRole.User, "任务");
        ChatMessage oldReport = new(ChatRole.User, "旧结论");
        ChatMessage replaced = new(ChatRole.User, "新结论");
        List<ConversationItemBase> items = [Item(question, true), Item(oldReport)];

        Assert.NotNull(ConversationOrderCheck.FindDivergence(items, [question, replaced]));
    }

    /// <summary>
    /// 上一个用例的另一半：光看尾部会报 1（新结论确实没画），直接追加就会旧行、新行并存。
    /// 所以对账必须先查分歧（全量重放把旧行换掉），再查尾部——顺序反了就是重复。
    /// </summary>
    [Fact]
    public void AReplacedTailMustDivergeBeforeAppending()
    {
        ChatMessage question = new(ChatRole.User, "任务");
        ChatMessage oldReport = new(ChatRole.User, "旧结论");
        ChatMessage replaced = new(ChatRole.User, "新结论");
        List<ConversationItemBase> items = [Item(question, true), Item(oldReport)];

        Assert.Equal(1, ConversationOrderCheck.FindMissingTail(items, [question, replaced]));
        Assert.NotNull(ConversationOrderCheck.FindDivergence(items, [question, replaced]));
    }

    /// <summary>同一条消息拆成多个条目（思考卡 + 正文）是常态，不是分歧</summary>
    [Fact]
    public void SeveralItemsSharingOneSourceAreInOrder()
    {
        ChatMessage message = new(ChatRole.Assistant, "回答");
        List<ConversationItemBase> items = [Item(message), Item(message)];

        Assert.Null(ConversationOrderCheck.FindDivergence(items, [message]));
    }

    /// <summary>正文相同的两条消息是两条：认人靠引用，合并了顺序就判不出来</summary>
    [Fact]
    public void MessagesWithTheSameTextAreToldApart()
    {
        ChatMessage earlier = new(ChatRole.User, "555");
        ChatMessage later = new(ChatRole.User, "555");
        List<ConversationItemBase> items = [Item(later, true), Item(earlier, true)];

        Assert.NotNull(ConversationOrderCheck.FindDivergence(items, [earlier, later]));
    }

    /// <summary>尾部多出来的历史就是漏画：交回报告与唤醒回复落了盘、界面一条没加正是这个形状</summary>
    [Fact]
    public void TrailingHistoryWithoutItemsIsMissing()
    {
        ChatMessage question = new(ChatRole.User, "任务");
        ChatMessage answer = new(ChatRole.Assistant, "派人去查了");
        ChatMessage report = new(ChatRole.User, "后续结论");
        ChatMessage reply = new(ChatRole.Assistant, "整理版");
        List<ConversationItemBase> items = [Item(question, true), Item(answer)];

        Assert.Equal(2, ConversationOrderCheck.FindMissingTail(items, [question, answer, report, reply]));
    }

    /// <summary>对得上时是 0；窗口之外的旧消息本来就不画，不算漏</summary>
    [Fact]
    public void DrawnTailIsNotMissing()
    {
        ChatMessage old = new(ChatRole.User, "窗口之外的旧消息");
        ChatMessage question = new(ChatRole.User, "任务");
        ChatMessage answer = new(ChatRole.Assistant, "回答");
        List<ConversationItemBase> items = [Item(question, true), Item(answer)];

        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, [old, question, answer]));
    }

    /// <summary>还没配对的气泡（来源是空）不参与判定，否则每轮直播都要误判一次</summary>
    [Fact]
    public void UnpairedBubblesDoNotCountAsDrawn()
    {
        ChatMessage question = new(ChatRole.User, "任务");
        List<ConversationItemBase> items = [Item(question, true), Item(null, true)];

        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, [question]));
    }

    /// <summary>
    /// 交接文档落在历史中段、却漏画时，两条判据都发现不了：尾部已画到最后一条、
    /// 已画条目的来源顺序也单调。这是压缩期间并发新轮场景下的已知盲区——
    /// 兜底在 HandoffWritten 通知里（ConversationViewModel），对账救不回。
    /// 钉成已知行为，免得将来有人拿对账去兜它。
    /// </summary>
    [Fact]
    public void AMiddleNoteMissingIsInvisibleToReconciliation()
    {
        ChatMessage first = new(ChatRole.User, "任务");
        ChatMessage note = new(ChatRole.System, "[Context handoff]\n交接正文");
        ChatMessage later = new(ChatRole.User, "后续");
        List<ConversationItemBase> items = [Item(first, true), Item(later, true)]; //note 漏画

        Assert.Equal(0, ConversationOrderCheck.FindMissingTail(items, [first, note, later]));
        Assert.Null(ConversationOrderCheck.FindDivergence(items, [first, note, later]));
    }
}
