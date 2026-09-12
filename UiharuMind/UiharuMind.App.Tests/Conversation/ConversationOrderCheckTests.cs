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

    /// <summary>窗口之外（或已被删掉）的来源不参与判定：它本就不在这份历史里</summary>
    [Fact]
    public void ASourceOutsideTheWindowIsIgnored()
    {
        ChatMessage inWindow = new(ChatRole.Assistant, "回答");
        ChatMessage outside = new(ChatRole.User, "更早的问题");
        List<ConversationItemBase> items = [Item(outside, true), Item(inWindow)];

        Assert.Null(ConversationOrderCheck.FindDivergence(items, [inWindow]));
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
}
