/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 消息级操作要回头请视图模型做的事。
///
/// 提成接口而不是几个 <see cref="Func{TResult}"/>：这里已经有五件事，
/// 再散成五个委托，构造处会变成一串看不出谁是谁的 lambda。
/// </summary>
public interface IConversationItemActionHost
{
    /// <summary>当前会话;无会话为 null</summary>
    ChatSession? Session { get; }

    /// <summary>本轮是否正在跑(跑着的时候不接受重试)</summary>
    bool IsGenerating { get; }

    /// <summary>以某条历史消息为输入重跑一轮</summary>
    /// <param name="input">用户消息;null = 无输入续写(历史停在截断处,让模型接着生成)</param>
    void Rerun(ChatMessage? input);

    /// <summary>会话集合变化(分叉出了新会话)</summary>
    void NotifySessionsChanged();

    /// <summary>条目接线完毕——接线不触发集合事件，可重新生成的判据要手动刷</summary>
    void NotifyItemsWired();
}

/// <summary>
/// 气泡上那一行操作：编辑 / 删除 / 分叉 / 重试，以及「谁有资格显示它们」。
///
/// 这四个动作是<b>唯一会改写 <c>session.History</c> 并落盘的地方</b>，
/// 集中在一处才看得清「哪些操作会动存档」。
/// </summary>
public sealed class ConversationItemActions
{
    private readonly ObservableCollection<ConversationItemBase> _items;
    private readonly IConversationItemActionHost _host;
    private readonly IMessageService? _messageService;

    /// <param name="items">界面条目集合(与视图模型共用同一个实例)</param>
    /// <param name="host">要回头请视图模型做的那几件事</param>
    /// <param name="messageService">删除确认弹窗;省略则首次删除时从容器取(惰性,测试可缺)</param>
    public ConversationItemActions(ObservableCollection<ConversationItemBase> items,
        IConversationItemActionHost host, IMessageService? messageService = null)
    {
        _items = items;
        _host = host;
        _messageService = messageService;
    }

    /// <summary>
    /// 给条目接上编辑/删除/分叉/重试。只有能定位回历史消息的条目才提供这些操作，
    /// 因此流式进行中的占位条目与框架注入的内容不会出现这些按钮。
    /// </summary>
    /// <param name="item">条目</param>
    /// <param name="source">来源消息</param>
    /// <returns>原条目，便于内联使用</returns>
    public T Wire<T>(T item, ChatMessage source) where T : ConversationItemBase
    {
        item.SourceMessage = source;
        // 群发言送达即不可改(ADR 0046 决策 6):改群流水改不到已经交出去的那几份;
        // 成员会话在骨架里只读。两处都只留来源,不挂任何改写历史的动作
        if (_host.Session is { IsGroup: true } or { IsGroupMember: true }) return item;
        // 点名调用的气泡显示的是 /技能名 那一行,而消息正文是注入的技能全文;
        // 放开编辑会把正文改写成那一行,当场毁掉注入内容
        if (NamedSkillAnnotations.InputOf(source) == null) item.EditedCallback = OnEdited;
        item.DeleteCallback = OnDeleted;
        // 旁白(开场白)不给分叉:它是历史的第一条,"从这里分出去"就是新建一个会话
        if (!ChatMessageAnnotations.IsNarration(source)) item.BranchCallback = OnBranch;
        // 重试语义是"从这条输入起重新生成":用户消息以自己为锚,助手消息回落到它前面最近的用户输入;
        // 旁白(开场白/子代理报告)没有对应的提问,不给重试
        if (!ChatMessageAnnotations.IsNarration(source)
            && (source.Role == ChatRole.User || source.Role == ChatRole.Assistant))
        {
            item.RetryCallback = Retry;
        }
        return item;
    }

    /// <summary>
    /// 一轮结束后（或跑到一次落盘），历史已由提供器写入。
    /// 把界面上刚产出的、来源还没落到历史里的文本气泡按角色与历史尾部配对，使其也能被操作。
    ///
    /// 「已配对」的判据是<b>来源就在历史里</b>，不是「来源非空」：实时画出来的用户气泡
    /// 接的是发送时那个实例，而框架交给持久化的是重建的副本——只看非空会让它永远指着
    /// 一条不在历史里的消息，编辑/删除因此静默失效。
    /// </summary>
    /// <param name="history">当前历史</param>
    public void WireStreamed(IReadOnlyList<ChatMessage> history)
    {
        HashSet<ChatMessage> persisted = new(history, ReferenceEqualityComparer.Instance);
        int cursor = history.Count - 1;

        for (int i = _items.Count - 1; i >= 0 && cursor >= 0; i--)
        {
            if (_items[i] is not TextConversationItem item) continue;
            if (item.SourceMessage is { } source && persisted.Contains(source)) break; //再往前都是配好的

            // 只在角色一致时配对,不一致说明界面与历史的形状对不上,宁可不提供操作
            ChatRole expected = item.IsUser ? ChatRole.User : ChatRole.Assistant;
            int candidate = FindPairingCandidate(history, cursor, item, expected);
            if (candidate < 0) break;
            cursor = candidate;

            // 用户气泡再问一句「正文对得上吗」:形状对不上时宁可不接,接错了编辑/删除会改错消息。
            // 助手气泡不做这一道:正文是流式攒的,与落盘那份未必逐字相同
            if (item.IsUser &&
                !string.Equals(ConversationItemFactory.DisplayTextOf(history[cursor]), item.Message,
                    StringComparison.Ordinal))
            {
                break;
            }

            Wire(item, history[cursor]);
            cursor--;
        }

        AttachStreamedSources(history);
        _host.NotifyItemsWired();
    }

    /// <summary>
    /// 从 <paramref name="cursor"/> 起往前找能与这只气泡配对的历史消息。
    ///
    /// 助手气泡优先认“真有正文”的那条：纯思考收尾的消息也是 <c>Assistant</c>，
    /// 只看角色会一路摸到尾、把正文气泡指到一条没有正文的消息上——编辑会改错地方，
    /// 思考卡再按顺序也只能认到前一条，两边一交叉，对账接着就报分歧要求全量重放。
    /// 实在没有带正文的才回落到只看角色（形状已经对不上了，配上总比空着强）。
    /// 用户气泡不走这一道：它有正文比对兜底，角色一致即候选。
    /// </summary>
    /// <param name="history">当前历史</param>
    /// <param name="cursor">从这里（含）往前找</param>
    /// <param name="item">待配对的气泡</param>
    /// <param name="expected">期望的角色</param>
    /// <returns>配对消息的下标；找不到为 -1</returns>
    private static int FindPairingCandidate(IReadOnlyList<ChatMessage> history, int cursor,
        TextConversationItem item, ChatRole expected)
    {
        if (item.IsUser)
        {
            while (cursor >= 0 && history[cursor].Role != expected) cursor--;
            return cursor;
        }

        int fallback = -1;
        while (cursor >= 0)
        {
            if (history[cursor].Role == expected)
            {
                if (fallback < 0) fallback = cursor;
                if (HasText(history[cursor])) return cursor;
            }

            cursor--;
        }

        return fallback;
    }

    /// <summary>这条历史消息有没有能画成正文气泡的正文</summary>
    /// <param name="message">历史消息</param>
    /// <returns>有非空正文则为 true</returns>
    private static bool HasText(ChatMessage message)
    {
        foreach (TextContent text in message.Contents.OfType<TextContent>())
        {
            if (!string.IsNullOrWhiteSpace(text.Text)) return true;
        }

        return false;
    }

    /// <summary>
    /// 给流式产出的非文本条目补上来源消息。
    ///
    /// 删除是按「来源落在删除集合里」摘条目的，漏记来源的条目会在来源消失后
    /// 变成删不掉的残留（思考卡、工具卡都没有自己的删除按钮）。
    ///
    /// 工具卡按 <c>CallId</c> 精确回指发起它的那条消息；其余条目回落到紧随其后的
    /// 条目的来源。回落是猜的，但<b>猜错只会影响界面</b>——它顶多让一张卡片跟着
    /// 同一轮里相邻的消息一起消失，而历史侧删哪些由
    /// <see cref="HistoryEditRange"/> 按 CallId 独立算出，不受这里的归属影响。
    /// </summary>
    /// <param name="history">当前历史</param>
    private void AttachStreamedSources(IReadOnlyList<ChatMessage> history)
    {
        Dictionary<string, ChatMessage> callOwners = new();
        foreach (ChatMessage message in history)
        {
            foreach (FunctionCallContent call in message.Contents.OfType<FunctionCallContent>())
            {
                if (!string.IsNullOrEmpty(call.CallId)) callOwners[call.CallId] = message;
            }
        }

        // 倒着走:回落要读后一个条目的来源,它此时已经归属完毕
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            ConversationItemBase item = _items[i];
            if (item.SourceMessage != null) continue;

            if (item is ToolCallItem { CallId.Length: > 0 } card &&
                callOwners.TryGetValue(card.CallId!, out ChatMessage? owner))
            {
                item.SourceMessage = owner;
                continue;
            }

            if (item is ThinkingItem) continue; //思考卡走下面的精确配对，不在这里猜

            if (i + 1 < _items.Count) item.SourceMessage = _items[i + 1].SourceMessage;
        }

        PairStreamedThinking(history);
    }

    /// <summary>
    /// 给流式产出的思考卡补上来源消息。
    ///
    /// 文本气泡按角色配对、工具卡按 <c>CallId</c> 精确回指，思考卡两样都沾不上：
    /// 它不是气泡、也没有 <c>CallId</c>。原来只能“回落到后一条”，当一轮以纯思考收尾
    /// （有推理、无正文、无工具调用）时它正好是尾巴，后面没有可回落的条目，
    /// 来源永远是空——对账于是把它当成“没画”，按历史又追加一张，
    /// 同一段思考在界面上出现两次（实机踩到）。
    ///
    /// 三阶段认领，每一阶段都不许交叉（显示在前的卡只能认更早或同时的消息）：
    /// <list type="number">
    /// <item>先按全文长度精确配对。直播缓冲与落盘文本逐字一致（规整只删空增量、合并碎片，
    /// 见 <c>ChatContentNormalizer</c>），长度对上就是同一段思考。</item>
    /// <item>剩下的按顺序配对（尽力而为）；实在没有位置了就跟最近认走的那条抱团——
    /// 同一条消息本来就会拆出多张卡（思考/正文交替），抱团与回放形状一致。</item>
    /// <item>历史里根本没有带推理的消息时（如取消打断的半截思考），沿用原来的回落：
    /// 隔壁条目的来源，至少保证卡片能跟着删。</item>
    /// </list>
    ///
    /// 长度与顺序打架时顺序优先：交叉的归属不仅删错轮次，还会让对账报出假分歧
    /// （错的两张卡在历史里一前一后）；顺序一致的误配顶多是隔壁两轮抱团，
    /// 删不错地方。配错的代价 ceiling 都是界面归属——历史侧删哪些由
    /// <see cref="HistoryEditRange"/> 独立算出（与工具卡的回落同口径）。
    /// </summary>
    /// <param name="history">当前历史</param>
    private void PairStreamedThinking(IReadOnlyList<ChatMessage> history)
    {
        List<ThinkingItem> unwired = new();
        foreach (ConversationItemBase item in _items)
        {
            if (item is ThinkingItem thinking && thinking.SourceMessage == null) unwired.Add(thinking);
        }

        if (unwired.Count == 0) return;

        HashSet<ChatMessage> claimed = new(ReferenceEqualityComparer.Instance);
        foreach (ConversationItemBase item in _items)
        {
            if (item is ThinkingItem thinking && thinking.SourceMessage != null) claimed.Add(thinking.SourceMessage);
        }

        List<(ChatMessage Message, int Index, int ReasoningLength)> candidates = new();
        for (int i = 0; i < history.Count; i++)
        {
            ChatMessage message = history[i];
            if (message.Role != ChatRole.Assistant || claimed.Contains(message)) continue;
            int length = 0;
            foreach (TextReasoningContent reasoning in message.Contents.OfType<TextReasoningContent>())
            {
                length += reasoning.Text?.Length ?? 0;
            }

            if (length > 0) candidates.Add((message, i, length));
        }

        int assigned = -1; //已认走的最靠后的历史下标：之后的所有认领都不许越过它回头
        if (candidates.Count > 0)
        {
            foreach (ThinkingItem thinking in unwired)
            {
                int length = thinking.FullLength;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Index < assigned || candidates[i].ReasoningLength != length) continue;
                    thinking.SourceMessage = candidates[i].Message;
                    assigned = candidates[i].Index;
                    candidates.RemoveAt(i);
                    break;
                }
            }

            foreach (ThinkingItem thinking in unwired)
            {
                if (thinking.SourceMessage != null) continue;
                int slot = candidates.FindIndex(x => x.Index >= assigned);
                if (slot < 0)
                {
                    if (assigned < 0) break;
                    thinking.SourceMessage = history[assigned];
                    continue;
                }

                thinking.SourceMessage = candidates[slot].Message;
                assigned = candidates[slot].Index;
                candidates.RemoveAt(slot);
            }
        }

        // 前两阶段都没认出来：历史里没有可认的推理消息。沿用原来的回落，
        // 尾巴上后面没有条目时仍是空——老代码同样是空，没有退化。
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] is not ThinkingItem thinking || thinking.SourceMessage != null) continue;
            if (i + 1 < _items.Count) thinking.SourceMessage = _items[i + 1].SourceMessage;
        }
    }

    private void OnEdited(ConversationItemBase item)
    {
        if (item.SourceMessage == null) return;

        // 就地改写 TextContent:ChatMessage.Text 是只读的(所有 TextContent 的拼接),
        // 且不能整体替换 Contents,否则会丢掉同一条消息里的图片
        TextContent? text = item.SourceMessage.Contents.OfType<TextContent>().FirstOrDefault();
        if (text != null) text.Text = item.Message;
        else item.SourceMessage.Contents.Add(new TextContent(item.Message));

        _host.Session?.Save();
    }

    private async Task OnDeleted(ConversationItemBase item)
    {
        ChatSession? session = _host.Session;
        if (session == null || item.SourceMessage == null) return;

        // 历史侧删哪些,由配对闭包算出:调用与结果成对进出,不可能留下悬空的一头。
        // 语义因此是"删掉这条消息,外加它绑着的工具往返"——不多不少,
        // 不会顺手吞掉后面那些配对完整、本可留下的执行痕迹
        IReadOnlyList<ChatMessage> doomed = HistoryEditRange.ResolveDeletion(session.History, item.SourceMessage);
        if (doomed.Count == 0) return;

        // 界面侧不另算一套区间:凡来源落在同一个删除集合里的条目一起摘掉。
        // 两侧共用<b>同一个判据</b>,历史与界面因此不可能各删各的——
        // 一条消息拆出的多个气泡(正文被工具调用截断)、以及与正文同源的思考卡,
        // 都由这一条规则一并带走,不需要各自的特例
        HashSet<ChatMessage> doomedSet = new(doomed);
        List<ConversationItemBase> targets = _items
            .Where(x => x.SourceMessage != null && doomedSet.Contains(x.SourceMessage))
            .ToList();

        // 提示用<b>界面实际会删的条目数</b>——历史消息数与界面条目数不对应
        // (一条含思考+正文的消息 = 两个条目,一次工具往返两条历史 = 一张卡),报历史条数用户数不上
        IMessageService messageService = _messageService ?? App.Services.GetRequiredService<IMessageService>();
        string confirmText = targets.Count > 1
            ? string.Format(Loc.Text(LangKey.MessageDeleteTurnConfirmFormat), targets.Count)
            : Loc.Text(LangKey.MessageDeleteConfirm);
        if (!await messageService.ConfirmAsync(confirmText)) return;

        // 删除集合在弹窗之前就算好了,但它装的是消息与条目的<b>实例</b>而不是下标——
        // 等待确认期间即便有新一轮落盘、追加了消息与条目,这里也不会误伤
        session.History.RemoveAll(doomedSet.Contains);
        session.Save();
        RemoveItems(targets);
    }

    /// <summary>
    /// 把一批条目摘出界面。倒序遍历：<c>RemoveAt</c> 会压缩下标，正序删同一批下标
    /// 会把后面的条目一路全删掉。
    /// 释放排在摘除之后：还挂在界面上的位图一释放，下一帧渲染就撞上去。
    /// </summary>
    /// <param name="targets">要摘掉的条目</param>
    private void RemoveItems(List<ConversationItemBase> targets)
    {
        HashSet<ConversationItemBase> doomed = new(targets);
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (doomed.Contains(_items[i])) _items.RemoveAt(i);
        }

        foreach (ConversationItemBase item in targets) item.ReleaseImages();
    }

    private void OnBranch(ConversationItemBase item)
    {
        ChatSession? session = _host.Session;
        if (session == null || item.SourceMessage == null) return;

        int index = session.History.IndexOf(item.SourceMessage);
        if (index < 0) return;

        ChatSession branch = SessionManager.Instance.DeepCopy(session);
        branch.SessionId = Guid.NewGuid().ToString("N");
        branch.Title = $"{session.Title} {Loc.Text(LangKey.ChatBranchSuffix)}";
        branch.CreatedAt = DateTimeOffset.Now;
        // 附件文件仍归原会话所有:两边都登记会导致删除任一方时打断另一方
        branch.OwnedAttachmentFiles.Clear();
        // 保留到该条消息为止。截断点后移到配对闭合处:分叉点常常正好落在一条
        // 「正文 + 工具调用」消息之后、它的结果之前,照原样截会让分出去的会话一开口就 400
        int keep = HistoryEditRange.ExpandKeptPrefix(branch.History, index + 1);
        branch.History.RemoveRange(keep, branch.History.Count - keep);
        SessionManager.Instance.Add(branch);
        _host.NotifySessionsChanged();
    }

    /// <summary>
    /// 一条助手消息重试时,被删掉的条数达到这个值就先弹确认。
    /// 重试 = 替换它及之后的内容,它后面还有一长串(工具往返/后续对话)时,
    /// 静默全删就像“聊天丢了”——删得多就该先问一声。
    /// </summary>
    private const int RetryConfirmThreshold = 4;

    /// <summary>
    /// 从某条消息起重新生成。也是「重新生成上一条」那个命令的落点。
    ///
    /// 用户消息以自己为锚:删掉它及之后,再以它为输入重跑一轮(提问本体由重跑写回)。
    /// 助手消息同样以自己为锚:删掉它及之后,历史停在它前面的内容,由无输入轮续写新回复——
    /// 不再回溯到它前面的提问重跑整轮(那样会把提问和更早的对话一起卷进去,
    /// 重试一条靠前的回复就退回到整个对话开头)。
    /// 删掉的条数多时先弹确认。
    /// </summary>
    /// <param name="item">条目</param>
    public async Task Retry(ConversationItemBase item)
    {
        ChatSession? session = _host.Session;
        if (session == null || item.SourceMessage == null || _host.IsGenerating) return;

        int index = session.History.IndexOf(item.SourceMessage);
        if (index < 0) return;

        // 从这条消息起删(含它自己):重试 = 替换它及之后的内容
        int doomedCount = session.History.Count - index;
        if (item.SourceMessage.Role == ChatRole.Assistant && doomedCount >= RetryConfirmThreshold)
        {
            IMessageService messageService = _messageService ?? App.Services.GetRequiredService<IMessageService>();
            string confirmText = string.Format(Loc.Text(LangKey.MessageRetryTurnConfirmFormat), doomedCount);
            if (!await messageService.ConfirmAsync(confirmText)) return;
        }

        ChatMessage input = session.History[index];
        session.History.RemoveRange(index, doomedCount);
        session.Save();

        // 界面侧从<b>该条气泡</b>起删:用户与助手都以自己为锚,不需再前移到提问气泡
        int itemIndex = _items.IndexOf(item);
        if (itemIndex >= 0)
        {
            // 截断的这一段条目不再回来,连它们气泡里的图一起释放(先摘出集合再释放)
            List<ConversationItemBase> discarded = new();
            for (int i = _items.Count - 1; i >= itemIndex; i--)
            {
                discarded.Add(_items[i]);
                _items.RemoveAt(i);
            }

            foreach (ConversationItemBase discardedItem in discarded) discardedItem.ReleaseImages();
        }

        if (item.SourceMessage.Role == ChatRole.Assistant)
        {
            // 无输入续写:提问与更早的对话都留在历史里,模型基于它们直接生成新回复
            _host.Rerun(null);
        }
        else
        {
            // 用户消息:提问由这轮请求写回(与发送同一条路)
            _items.Add(Wire(ConversationItemFactory.CreateUser(
                ConversationItemFactory.DisplayTextOf(input), input), input));
            _host.Rerun(input);
        }
    }
}
