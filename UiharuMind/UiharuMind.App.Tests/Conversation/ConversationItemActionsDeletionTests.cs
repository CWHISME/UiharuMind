using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 删除一条消息时的语义。
///
/// 一条规则：<b>删掉这条消息，外加它绑着的工具往返</b>——调用与结果成对进出，
/// 删完不会留下悬空的一头（留一头，这个会话下一次请求就 400）。配对之外一概不多删：
/// 后面那些配对完整的执行痕迹本可以留下，顺手吞掉就是用户没要求的数据丢失。
///
/// 界面侧不另算区间：凡来源落在同一个删除集合里的条目一起摘掉。历史与界面共用
/// 同一个判据，因此不可能各删各的——这也顺带解决了「一条消息拆成多个气泡」
/// 与「思考卡与正文同源」两类残留，不需要各自的特例。
///
/// 删除前都有确认弹窗（要删不止一个条目时报出条目数）。
/// </summary>
public class ConversationItemActionsDeletionTests
{
    /// <summary>
    /// 记录 ReleaseImages 的条目替身。
    /// 不用真的 <c>TextConversationItem</c> 加真位图:那要拉起 Avalonia 的图像后端,
    /// 而这里要验的其实只是那条<b>顺序约束</b>——释放必须发生在条目已经从集合里摘掉之后。
    /// </summary>
    private sealed class ProbeItem(ObservableCollection<ConversationItemBase> owner, bool isUser = false)
        : ConversationItemBase
    {
        public override bool IsUser => isUser;
        public int ReleaseCount { get; private set; }

        /// <summary>释放那一刻是否还挂在集合上（应当为 false）</summary>
        public bool WasStillAttachedOnRelease { get; private set; }

        public override void ReleaseImages()
        {
            ReleaseCount++;
            if (owner.Contains(this)) WasStillAttachedOnRelease = true;
        }
    }

    private sealed class StubHost(ChatSession session) : IConversationItemActionHost
    {
        public ChatSession? Session => session;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    /// <summary>删除确认 stub;记录是否真的问过、以及拒绝时的行为</summary>
    private sealed class ConfirmStub : IMessageService
    {
        public int ConfirmCount { get; private set; }
        public bool Result { get; init; } = true;
        public string? LastMessage { get; private set; }

        /// <summary>提示里报出的条目数;没报数(单条删除)时为 1</summary>
        public int LastCount { get; private set; }

        public Task<bool> ConfirmAsync(string message, string? title = null,
            System.Threading.CancellationToken ct = default)
        {
            ConfirmCount++;
            LastMessage = message;
            LastCount = int.TryParse(new string(message.Where(char.IsDigit).ToArray()), out int count) ? count : 1;
            return Task.FromResult(Result);
        }

        public Task ShowInfoAsync(string message, string? title = null,
            System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task ShowWarningAsync(string message, string? title = null,
            System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string message, string? title = null,
            System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<EConfirmChoice> ConfirmWithCancelAsync(string message, string? title = null,
            System.Threading.CancellationToken ct = default) => Task.FromResult(EConfirmChoice.Yes);
        public void ShowNotification(string message, string? title = null,
            MessageSeverity severity = MessageSeverity.Information) { }
    }

    private static ChatSession TransientSession()
    {
        return new ChatSession { IsTransient = true }; //Save 空操作,不落盘
    }

    private static ChatMessage UserMessage(string text) => new(ChatRole.User, text);
    private static ChatMessage AssistantText(string text) => new(ChatRole.Assistant, text);
    private static ChatMessage ToolCallMessage(string callId) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, "read_file", null)]);
    private static ChatMessage ToolResultMessage(string callId) =>
        new(ChatRole.Tool, [new FunctionResultContent(callId, "ok")]);

    /// <summary>
    /// 删用户提问只删自己:留下的工具往返是成对的,不会 400。
    /// 后面的回复与工具保留,用户问的"帮我改代码"消失,但执行痕迹还在。
    /// </summary>
    [Fact]
    public async Task DeletingUserMessage_KeepsTheToolTrafficAfter()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("帮我改代码");
        ChatMessage call = ToolCallMessage("c1");
        ChatMessage result = ToolResultMessage("c1");
        ChatMessage reply0 = AssistantText("改好了");
        ChatMessage user1 = UserMessage("下一个问题");
        ChatMessage reply1 = AssistantText("回答2");
        session.History.AddRange([user0, call, result, reply0, user1, reply1]);

        ProbeItem userBubble0 = actions.Wire(new ProbeItem(items, isUser: true), user0);
        items.Add(userBubble0);
        items.Add(new ToolCallItem { CallId = "c1", ToolName = "read_file" });
        ProbeItem replyBubble0 = actions.Wire(new ProbeItem(items), reply0);
        items.Add(replyBubble0);
        ProbeItem userBubble1 = actions.Wire(new ProbeItem(items, isUser: true), user1);
        items.Add(userBubble1);
        ProbeItem replyBubble1 = actions.Wire(new ProbeItem(items), reply1);
        items.Add(replyBubble1);

        await userBubble0.DeleteCommand.ExecuteAsync(null);

        // 历史:只删 user0,其余全部保留
        Assert.Equal(5, session.History.Count);
        Assert.Same(call, session.History[0]);
        Assert.Same(result, session.History[1]);
        Assert.Same(reply0, session.History[2]);
        Assert.Same(user1, session.History[3]);
        Assert.Same(reply1, session.History[4]);

        // 界面:只删用户气泡
        Assert.Equal(4, items.Count);
        Assert.DoesNotContain(userBubble0, items);
        Assert.Contains(replyBubble0, items);
        Assert.Contains(userBubble1, items);
        Assert.Contains(replyBubble1, items);
        Assert.Equal(1, userBubble0.ReleaseCount);
        Assert.Equal(0, replyBubble0.ReleaseCount);
        Assert.Equal(0, replyBubble1.ReleaseCount);
        Assert.Equal(1, confirms.ConfirmCount);
    }

    /// <summary>
    /// 普通聊天的纯文本回复:删用户消息仍保留角色的回复,那是既有行为
    /// </summary>
    [Fact]
    public async Task DeletingUserMessage_WithOnlyTextReplyAfter_KeepsTheReply()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("你好");
        ChatMessage reply0 = AssistantText("你好呀");
        session.History.AddRange([user0, reply0]);

        ProbeItem userBubble0 = actions.Wire(new ProbeItem(items, isUser: true), user0);
        items.Add(userBubble0);
        ProbeItem replyBubble0 = actions.Wire(new ProbeItem(items), reply0);
        items.Add(replyBubble0);

        await userBubble0.DeleteCommand.ExecuteAsync(null);

        Assert.Single(session.History);
        Assert.Same(reply0, session.History[0]);
        Assert.Single(items);
        Assert.Contains(replyBubble0, items);
        Assert.Equal(0, replyBubble0.ReleaseCount); //没被碰
        Assert.Equal(1, confirms.ConfirmCount);
    }

    /// <summary>
    /// 同一条 assistant 历史消息可能同时含思考与正文,回放时被拆成<b>两个条目</b>:
    /// <see cref="ThinkingItem"/> 排在前面、正文气泡排在后面。删除正文气泡时,
    /// 排在它前面的思考条目必须跟着一起消失——否则它没有来源消息(没接删除回调),
    /// 删除区间又只从正文气泡自己开始,思考就成了删不掉的残留。
    /// </summary>
    [Fact]
    public async Task DeletingAssistantBubble_RemovesThinkingItemFromTheSameMessage()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        // 一条 assistant 消息自带思考 + 正文
        ChatMessage user0 = UserMessage("帮我改代码");
        ChatMessage assistant = new(ChatRole.Assistant,
        [
            new TextReasoningContent("让我想想…"),
            new TextContent("改好了"),
        ]);
        ChatMessage user1 = UserMessage("下一个问题");
        session.History.AddRange([user0, assistant, user1]);

        ProbeItem userBubble0 = actions.Wire(new ProbeItem(items, isUser: true), user0);
        items.Add(userBubble0);
        ThinkingItem thinking = new();
        thinking.SourceMessage = assistant; //回放时与正文同一来源
        items.Add(thinking);
        ProbeItem assistantBubble = actions.Wire(new ProbeItem(items), assistant);
        items.Add(assistantBubble);
        ProbeItem userBubble1 = actions.Wire(new ProbeItem(items, isUser: true), user1);
        items.Add(userBubble1);

        await assistantBubble.DeleteCommand.ExecuteAsync(null);

        // 历史:assistant 那条消失,两端用户保留
        Assert.Equal(2, session.History.Count);
        Assert.Same(user0, session.History[0]);
        Assert.Same(user1, session.History[^1]);

        // 界面:同一消息的思考与正文一起消失
        Assert.Equal(2, items.Count);
        Assert.Contains(userBubble0, items);
        Assert.Contains(userBubble1, items);
        Assert.DoesNotContain(thinking, items);
        Assert.DoesNotContain(assistantBubble, items);
        Assert.Equal(1, confirms.ConfirmCount);
    }

    /// <summary>
    /// 实时流路径:一轮结束后 <see cref="ConversationItemActions.WireStreamed"/> 把正文气泡
    /// 与历史配对,紧贴在前的思考卡也要拿到同一来源——否则删除正文气泡时,
    /// 思考卡定位不到来源、没接删除回调,成为删不掉的残留。
    /// </summary>
    [Fact]
    public async Task DeletingStreamedBubble_RemovesThinkingItemWiredFromTheSameTurn()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("帮我改代码");
        ChatMessage assistant = new(ChatRole.Assistant,
        [
            new TextReasoningContent("让我想想…"),
            new TextContent("改好了"),
        ]);
        session.History.AddRange([user0, assistant]);

        // 实时流形态:思考卡在正文气泡前面,都还没接上来源
        items.Add(actions.Wire(new ProbeItem(items, isUser: true), user0));
        ThinkingItem thinking = new();
        items.Add(thinking);
        // 正文气泡必须是真正的 TextConversationItem:WireStreamed 只认这个类型
        TextConversationItem assistantBubble = new(false);
        items.Add(assistantBubble);

        actions.WireStreamed(session.History);

        // 正文与思考都拿到了同一来源
        Assert.Same(assistant, assistantBubble.SourceMessage);
        Assert.Same(assistant, thinking.SourceMessage);

        await assistantBubble.DeleteCommand.ExecuteAsync(null);

        Assert.Single(session.History);
        Assert.Same(user0, session.History[0]);

        // 界面:思考与正文一起消失,用户气泡保留
        Assert.Single(items);
        Assert.DoesNotContain(thinking, items);
        Assert.DoesNotContain(assistantBubble, items);
        Assert.Equal(1, confirms.ConfirmCount);
    }

    /// <summary>
    /// 删除确认被拒绝时,历史与界面都不动
    /// </summary>
    [Fact]
    public async Task Deleting_WhenUserCancels_ChangesNothing()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new() { Result = false };
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("帮我改代码");
        ChatMessage call = ToolCallMessage("c1");
        ChatMessage result = ToolResultMessage("c1");
        ChatMessage reply0 = AssistantText("改好了");
        session.History.AddRange([user0, call, result, reply0]);

        ProbeItem userBubble0 = actions.Wire(new ProbeItem(items, isUser: true), user0);
        items.Add(userBubble0);
        items.Add(new ToolCallItem { CallId = "c1", ToolName = "read_file" });
        ProbeItem replyBubble0 = actions.Wire(new ProbeItem(items), reply0);
        items.Add(replyBubble0);

        await userBubble0.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(4, session.History.Count); //什么都没删
        Assert.Equal(3, items.Count);
        Assert.Equal(1, confirms.ConfirmCount);
        Assert.Equal(0, userBubble0.ReleaseCount);
        Assert.Equal(0, replyBubble0.ReleaseCount);
    }


    /// <summary>
    /// 「正文 + 工具调用」同处一条 assistant 消息，是本仓最常见的真实形态。
    /// 删它<b>后面</b>那条回复时，前面那次调用的结果绝不能被顺手带走——
    /// 带走了就留下悬空的 c1，这个会话下一次请求直接 400。
    /// </summary>
    [Fact]
    public async Task DeletingReplyAfterToolTraffic_LeavesNoDanglingCall()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("做事");
        ChatMessage step = new(ChatRole.Assistant,
        [
            new TextContent("我来查一下"),
            new FunctionCallContent("c1", "read_file", null),
        ]);
        ChatMessage result = ToolResultMessage("c1");
        ChatMessage final = AssistantText("查完了");
        session.History.AddRange([user0, step, result, final]);

        items.Add(actions.Wire(new ProbeItem(items, isUser: true), user0));
        ProbeItem stepBubble = actions.Wire(new ProbeItem(items), step);
        items.Add(stepBubble);
        ToolCallItem card = new() { CallId = "c1", ToolName = "read_file", SourceMessage = step };
        items.Add(card);
        ProbeItem finalBubble = actions.Wire(new ProbeItem(items), final);
        items.Add(finalBubble);

        await finalBubble.DeleteCommand.ExecuteAsync(null);

        // 只删了那条回复,调用与结果原样成对留下
        Assert.Equal(3, session.History.Count);
        Assert.Same(step, session.History[1]);
        Assert.Same(result, session.History[2]);
        AssertNoDangling(session);

        Assert.Equal(3, items.Count);
        Assert.Contains(stepBubble, items);
        Assert.Contains(card, items);
        Assert.DoesNotContain(finalBubble, items);
    }

    /// <summary>
    /// 删「正文 + 工具调用」同体消息本身：它的结果必须一起走，否则结果成孤儿。
    /// 界面上那张工具卡也随之消失——它的来源就是这条消息。
    /// </summary>
    [Fact]
    public async Task DeletingReplyCarryingACall_TakesResultAndCardAlong()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("做事");
        ChatMessage step = new(ChatRole.Assistant,
        [
            new TextContent("我来查一下"),
            new FunctionCallContent("c1", "read_file", null),
        ]);
        ChatMessage result = ToolResultMessage("c1");
        ChatMessage final = AssistantText("查完了");
        session.History.AddRange([user0, step, result, final]);

        items.Add(actions.Wire(new ProbeItem(items, isUser: true), user0));
        ProbeItem stepBubble = actions.Wire(new ProbeItem(items), step);
        items.Add(stepBubble);
        ToolCallItem card = new() { CallId = "c1", ToolName = "read_file", SourceMessage = step };
        items.Add(card);
        ProbeItem finalBubble = actions.Wire(new ProbeItem(items), final);
        items.Add(finalBubble);

        await stepBubble.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, session.History.Count);
        Assert.Same(user0, session.History[0]);
        Assert.Same(final, session.History[1]);
        AssertNoDangling(session);

        // 界面:同源的正文气泡与工具卡一起消失,末尾那条回复保留
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(stepBubble, items);
        Assert.DoesNotContain(card, items);
        Assert.Contains(finalBubble, items);
        Assert.Equal(2, confirms.LastCount); //提示的是界面条目数
    }

    /// <summary>
    /// 一条 assistant 消息的正文被工具调用截断，回放时拆成<b>两个</b>正文气泡
    /// （见 <c>ConversationTranscript</c> 遇到调用即收段）。删其中任意一个，
    /// 整条消息连同它的两个气泡一起走——否则剩下的那个气泡来源已不在历史里，
    /// 点删除时定位不到，成为界面上删不掉的鬼条目。
    /// </summary>
    [Fact]
    public async Task DeletingOneOfTwoBubblesFromTheSameMessage_LeavesNoGhost()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("做事");
        ChatMessage mixed = new(ChatRole.Assistant,
        [
            new TextContent("先看看"),
            new FunctionCallContent("c1", "read_file", null),
            new TextContent("看完了"),
        ]);
        ChatMessage result = ToolResultMessage("c1");
        session.History.AddRange([user0, mixed, result]);

        items.Add(actions.Wire(new ProbeItem(items, isUser: true), user0));
        ProbeItem first = actions.Wire(new ProbeItem(items), mixed);
        items.Add(first);
        ToolCallItem card = new() { CallId = "c1", ToolName = "read_file", SourceMessage = mixed };
        items.Add(card);
        ProbeItem second = actions.Wire(new ProbeItem(items), mixed);
        items.Add(second);

        await second.DeleteCommand.ExecuteAsync(null);

        Assert.Single(session.History);
        Assert.Same(user0, session.History[0]);
        AssertNoDangling(session);

        // 同源的三个条目一起消失,不留鬼条目
        Assert.Single(items);
        Assert.DoesNotContain(first, items);
        Assert.DoesNotContain(second, items);
        Assert.DoesNotContain(card, items);
    }

    /// <summary>
    /// 历史里有、界面上没有的消息（框架注入的 todo 快照等，
    /// <c>ConversationItemFactory.IsFrameworkInjected</c> 不渲染任何条目）
    /// 不得让两侧错位：历史删了什么，界面就删什么，不多不少。
    /// </summary>
    [Fact]
    public async Task InvisibleHistoryMessages_DoNotDesyncItemsAndHistory()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("做事");
        ChatMessage reply1 = AssistantText("我来看看");
        ChatMessage injected = new(ChatRole.User, "[todo 快照]")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.Attribution] = "framework",
            },
        };
        ChatMessage call1 = ToolCallMessage("c1");
        ChatMessage result1 = ToolResultMessage("c1");
        ChatMessage reply2 = AssistantText("完成");
        session.History.AddRange([user0, reply1, injected, call1, result1, reply2]);

        items.Add(actions.Wire(new ProbeItem(items, isUser: true), user0));
        ProbeItem bubble1 = actions.Wire(new ProbeItem(items), reply1);
        items.Add(bubble1);
        ToolCallItem card = new() { CallId = "c1", ToolName = "read_file", SourceMessage = call1 };
        items.Add(card);
        ProbeItem bubble2 = actions.Wire(new ProbeItem(items), reply2);
        items.Add(bubble2);

        await bubble1.DeleteCommand.ExecuteAsync(null);

        // 历史只少了 reply1;工具往返仍在历史里,那张卡也就必须还在界面上
        Assert.Equal(5, session.History.Count);
        Assert.DoesNotContain(reply1, session.History);
        AssertNoDangling(session);

        Assert.Equal(3, items.Count);
        Assert.DoesNotContain(bubble1, items);
        Assert.Contains(card, items);
        Assert.Contains(bubble2, items);
    }

    /// <summary>
    /// 流式路径：本轮产出的工具卡按 <c>CallId</c> 回指发起它的那条消息，
    /// 思考卡回落到紧随其后的条目——都拿到来源，删除时才不会留下残留。
    /// </summary>
    [Fact]
    public async Task WireStreamed_AttachesSourcesToToolCardsAndThinking()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("做事");
        ChatMessage call1 = ToolCallMessage("c1");
        ChatMessage result1 = ToolResultMessage("c1");
        ChatMessage reply = new(ChatRole.Assistant,
        [
            new TextReasoningContent("让我想想…"),
            new TextContent("完成"),
        ]);
        session.History.AddRange([user0, call1, result1, reply]);

        items.Add(actions.Wire(new ProbeItem(items, isUser: true), user0));
        ToolCallItem card = new() { CallId = "c1", ToolName = "read_file" };
        items.Add(card);
        ThinkingItem thinking = new();
        items.Add(thinking);
        TextConversationItem bubble = new(false);
        items.Add(bubble);

        actions.WireStreamed(session.History);

        Assert.Same(call1, card.SourceMessage); //按 CallId 精确回指,不是猜的
        Assert.Same(reply, bubble.SourceMessage);
        Assert.Same(reply, thinking.SourceMessage); //回落到后一个条目的来源

        await bubble.DeleteCommand.ExecuteAsync(null);

        // 思考卡与正文同源,一起消失;工具往返成对留下
        Assert.DoesNotContain(thinking, items);
        Assert.DoesNotContain(bubble, items);
        Assert.Contains(card, items);
        AssertNoDangling(session);
    }

    /// <summary>确认弹窗报的是界面实际会摘掉的条目数，不是历史消息数</summary>
    [Fact]
    public async Task DeletingReply_ConfirmCountMatchesVisibleItems()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage user0 = UserMessage("做事");
        // 一条消息 = 思考卡 + 正文气泡 + 工具卡三个条目,历史侧却只有它与结果两条
        ChatMessage reply = new(ChatRole.Assistant,
        [
            new TextReasoningContent("让我想想…"),
            new TextContent("我来查一下"),
            new FunctionCallContent("c1", "read_file", null),
        ]);
        ChatMessage result = ToolResultMessage("c1");
        session.History.AddRange([user0, reply, result]);

        items.Add(actions.Wire(new ProbeItem(items, isUser: true), user0));
        items.Add(new ThinkingItem { SourceMessage = reply });
        ProbeItem bubble = actions.Wire(new ProbeItem(items), reply);
        items.Add(bubble);
        items.Add(new ToolCallItem { CallId = "c1", ToolName = "read_file", SourceMessage = reply });

        await bubble.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(3, confirms.LastCount);
        Assert.NotNull(confirms.LastMessage);
        Assert.Contains("3", confirms.LastMessage);
        Assert.Single(items);
        AssertNoDangling(session);
    }

    /// <summary>
    /// 内容完全相同的两条消息（同一句话问了两遍）必须按<b>引用</b>区分：
    /// 删其中一条不得连坐另一条。删除是按消息实例做集合运算的，
    /// 一旦 <c>ChatMessage</c> 走值相等，这里就会静默多删。
    /// </summary>
    [Fact]
    public async Task IdenticalMessages_AreDistinguishedByReference()
    {
        ChatSession session = TransientSession();
        ObservableCollection<ConversationItemBase> items = new();
        ConfirmStub confirms = new();
        ConversationItemActions actions = new(items, new StubHost(session), confirms);

        ChatMessage first = UserMessage("再来一次");
        ChatMessage between = AssistantText("好的");
        ChatMessage second = UserMessage("再来一次"); //与 first 内容一字不差
        session.History.AddRange([first, between, second]);

        ProbeItem firstBubble = actions.Wire(new ProbeItem(items, isUser: true), first);
        items.Add(firstBubble);
        items.Add(actions.Wire(new ProbeItem(items), between));
        ProbeItem secondBubble = actions.Wire(new ProbeItem(items, isUser: true), second);
        items.Add(secondBubble);

        await firstBubble.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(2, session.History.Count);
        Assert.Same(between, session.History[0]);
        Assert.Same(second, session.History[1]);
        Assert.Equal(2, items.Count);
        Assert.Contains(secondBubble, items);
    }

    /// <summary>
    /// 删完之后每个调用都有配对结果、每个结果都有配对调用。
    /// 破了它，这个会话下一次请求就 400。
    /// </summary>
    private static void AssertNoDangling(ChatSession session)
    {
        string[] calls = session.History.SelectMany(x => x.Contents)
            .OfType<FunctionCallContent>().Select(x => x.CallId).ToArray();
        string[] results = session.History.SelectMany(x => x.Contents)
            .OfType<FunctionResultContent>().Select(x => x.CallId).ToArray();

        Assert.Empty(calls.Except(results));
        Assert.Empty(results.Except(calls));
    }
}
