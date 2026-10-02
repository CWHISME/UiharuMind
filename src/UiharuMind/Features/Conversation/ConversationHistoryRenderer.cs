/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Features.Conversation.Group;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 把历史画进会话条目集合：回放一窗、往前续一窗、落盘后补渲染、原地替换一条。
/// 实时流由视图模型持有的那个转录器画，这里只管「从历史来」的那一半；两边对同一条消息的归属
/// 问的是同一份判据（<see cref="ConversationMessageOrigin"/>），用户气泡也共用 <see cref="CreateUserItems"/>
/// </summary>
public sealed class ConversationHistoryRenderer
{
    private readonly ObservableCollection<ConversationItemBase> _items;
    private readonly ConversationItemActions _itemActions; //给气泡接上来源（编辑/删除/分叉/重试）
    private readonly Func<CharacterData?> _characterSource; //会话角色：助手气泡的名字与头像
    private readonly Func<bool> _autoCollapseThinkingSource;
    private readonly Func<GroupDeliveryRenderer?> _deliverySource; //成员会话的群投递拆段；不是成员会话为 null
    private readonly Func<AgentPathResolver?> _pathsSource; //回放的生图、看图卡片按会话口径展开草稿目录简写

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="items">会话条目集合（界面绑的那一份）</param>
    /// <param name="itemActions">气泡上的消息级操作</param>
    /// <param name="characterSource">当前会话的角色</param>
    /// <param name="autoCollapseThinkingSource">思考段收尾时是否自动折叠</param>
    /// <param name="deliverySource">群投递渲染器</param>
    public ConversationHistoryRenderer(ObservableCollection<ConversationItemBase> items,
        ConversationItemActions itemActions, Func<CharacterData?> characterSource,
        Func<bool> autoCollapseThinkingSource, Func<GroupDeliveryRenderer?> deliverySource,
        Func<AgentPathResolver?> pathsSource)
    {
        _items = items;
        _itemActions = itemActions;
        _characterSource = characterSource;
        _autoCollapseThinkingSource = autoCollapseThinkingSource;
        _deliverySource = deliverySource;
        _pathsSource = pathsSource;
    }

    /// <summary>
    /// 用户消息 → 已接好来源的气泡。回放与实时（<see cref="UserMessageContent"/>）共用这一份，
    /// 两边因此对同一条消息画出同一个样子；框架注入的、空白无图的不画。
    /// 成员会话里的群投递拆成各发言人的气泡，所以是一组
    /// </summary>
    /// <param name="message">用户消息</param>
    /// <returns>气泡；这条不该显示则为空</returns>
    public IReadOnlyList<TextConversationItem> CreateUserItems(ChatMessage message)
    {
        string text = ConversationItemFactory.DisplayTextOf(message);
        if (ConversationItemFactory.IsFrameworkInjected(message)) return [];
        if (string.IsNullOrWhiteSpace(text) && !ConversationItemFactory.HasImage(message)) return [];

        if (_deliverySource() is { } renderer && renderer.IsDelivery(message))
        {
            return renderer.Render(message).Select(x => _itemActions.Wire(x, message)).ToList();
        }

        TextConversationItem item = ConversationItemFactory.CreateUser(text, message);
        // 化身替用户说的：成员看到的就是用户说的，这个标记只给用户回来复核、推翻用（ADR 0055）
        if (ChatMessageAnnotations.GroupAvatarPostOf(message) != null) item.SenderName = Loc.Text(LangKey.GroupAvatarSender);
        return [_itemActions.Wire(item, message)];
    }

    /// <summary>
    /// 把一段历史追加到条目集合末尾（切会话时回放首屏）
    /// </summary>
    /// <param name="messages">历史</param>
    /// <param name="from">起始下标</param>
    /// <param name="to">结束下标（不含）</param>
    /// <param name="liveTail">这一段是不是还在长的尾巴，见 <see cref="Build"/></param>
    public void Append(IReadOnlyList<ChatMessage> messages, int from, int to, bool liveTail)
    {
        foreach (ConversationItemBase item in Build(messages, from, to, liveTail))
        {
            _items.Add(item);
        }
    }

    /// <summary>
    /// 把一段历史前插到条目集合头部（往前续一窗）
    /// </summary>
    /// <param name="history">历史</param>
    /// <param name="range">要前插的区间</param>
    public void Prepend(IReadOnlyList<ChatMessage> history, (int From, int To) range)
    {
        List<ConversationItemBase> buffer = Build(history, range.From, range.To);
        for (int i = 0; i < buffer.Count; i++)
        {
            _items.Insert(i, buffer[i]);
        }
    }

    /// <summary>
    /// 历史被追加了一段（一次服务调用的落盘，或别处往这里写了东西）：按此刻谁在往界面流内容分三档补渲染
    /// </summary>
    /// <param name="history">历史</param>
    /// <param name="fromIndex">新增段的起始下标</param>
    /// <param name="ownTurn">视图自己驱动的那一轮正跑着</param>
    /// <param name="streaming">别处驱动的那一轮正往界面流内容</param>
    public void AppendPersisted(IReadOnlyList<ChatMessage> history, int fromIndex, bool ownTurn, bool streaming)
    {
        if (ownTurn) AppendHandedBackReports(history, fromIndex);
        else if (streaming) AppendAlongsideStream(history, fromIndex);
        else AppendWholeSlice(history, fromIndex);
    }

    /// <summary>
    /// 历史里的某一条被别处原地换掉了（后续报告替换了上一份）。
    ///
    /// <b>只重建那一条产出的条目</b>，不整份回放：markdown 是按条目、进视口才逐帧启用渲染器的
    /// （见 <c>SimpleMarkdownViewer</c>），清空重建等于让满屏气泡一起退回纯文本再一条条转回来
    /// ——用户看到的就是整个窗口闪一下。
    /// </summary>
    /// <param name="history">历史</param>
    /// <param name="index">被替换的下标</param>
    /// <param name="replaced">被换掉的那一条（靠它认回自己渲染出的条目）</param>
    public void Replace(IReadOnlyList<ChatMessage> history, int index, ChatMessage replaced)
    {
        // 旧那条产出的条目可能不止一个(工具卡、思考卡…),按来源整组认出来
        List<int> slots = new();
        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i].SourceMessage, replaced)) slots.Add(i);
        }

        // 那一条落在历史开窗之外(没渲染过),此刻也不该凭空补出来
        if (slots.Count == 0) return;

        List<ConversationItemBase> rebuilt = Build(history, index, index + 1);

        // 后续报告就是一条文本:能原地改就别动集合。摘掉再插回去会重建那一处的
        // markdown 渲染器(它按条目、进视口才启用),内容一字没变也要闪一下
        if (slots.Count == 1 && rebuilt is [TextConversationItem fresh] &&
            _items[slots[0]] is TextConversationItem existing)
        {
            fresh.Flush();
            existing.Message = fresh.Message;
            existing.Timestamp = fresh.Timestamp;
            _itemActions.Wire(existing, history[index]); //来源换人了,编辑/删除得指向新那条
            return;
        }

        for (int i = slots.Count - 1; i >= 0; i--)
        {
            _items.RemoveAt(slots[i]);
        }

        for (int i = 0; i < rebuilt.Count; i++)
        {
            _items.Insert(slots[0] + i, rebuilt[i]);
        }
    }

    /// <summary>
    /// 并发场景的交接卡兜底：压缩期间用户发了新消息，落盘路径不画交接文档，只有这里补画。
    /// 与 <see cref="Build"/> 的 HandoffNote 分支同源，不另写一份渲染逻辑
    /// </summary>
    /// <param name="history">历史</param>
    public void EnsureHandoffCard(IReadOnlyList<ChatMessage> history)
    {
        int index = HistoryHandoff.SupplyStartIndex(history);
        if (index < 0 || index >= history.Count) return;
        ChatMessage note = history[index];
        if (!HistoryHandoff.IsNote(note)) return; //没有交接文档时的兜底:SupplyStartIndex 无 note 返回 0
        if (IsRendered(note)) return; //已经画过就不再画
        Append(history, index, index + 1, liveTail: true);
    }

    /// <summary>
    /// 回放一段历史到独立缓冲：用一个不订阅用量的转录器实例装配，因此不会污染本轮/累计计数
    /// （累计口径由视图模型从会话本体恢复）。
    /// </summary>
    /// <param name="messages">历史</param>
    /// <param name="from">起始下标</param>
    /// <param name="to">结束下标（不含）</param>
    /// <param name="liveTail">
    /// 这一段是<b>还在长的尾巴</b>（外驱会话每次服务调用补渲染一段）而不是定格的历史。
    /// 此时：结果要能配回更早那批里的工具卡（调用与结果落在不同批），
    /// 且尚无结果的调用得继续转圈——按"历史里没有结果"收掉它就是谎报，
    /// 而下一批真把结果送来时卡片早已定格。
    /// </param>
    /// <returns>这一段产出的条目</returns>
    public List<ConversationItemBase> Build(IReadOnlyList<ChatMessage> messages, int from, int to,
        bool liveTail = false)
    {
        List<ConversationItemBase> buffer = new();
        CharacterData? sessionCharacter = _characterSource();
        CharacterData? speaker = sessionCharacter; //群流水里每条消息换一次发言人,工厂闭包取的是它
        ConversationTranscript replay = new(buffer, () => ConversationItemFactory.CreateAssistant(speaker),
            pathsSource: _pathsSource, renderedBefore: liveTail ? (IReadOnlyList<ConversationItemBase>)_items : null)
        {
            AutoCollapseThinking = _autoCollapseThinkingSource(),
        };

        // 回放时最近见过的时间戳。助手气泡的工厂给不出时间(它只造壳,拿不到源消息),
        // 默认填的是"现在"——重开会话时整段历史因此显示当前时刻。
        // 旧存档里框架产出的消息本就没有时间戳,那种回落到同一轮的用户消息,
        // 误差在一轮之内,总好过一个每次打开都变的假时间
        DateTimeOffset? lastKnown = null;

        for (int index = from; index < to; index++)
        {
            ChatMessage message = messages[index];
            lastKnown = message.CreatedAt ?? lastKnown;
            speaker = ChatMessageAnnotations.GroupSpeakerOf(message) is { } speakerId
                ? CharacterManager.Instance.GetCharacterData(speakerId)
                : sessionCharacter;
            // 渲染归属只有一份判据:哪些由内容流产出、哪些只能从历史来,
            // 实时流观察那条路问的是同一个函数(见 ConversationMessageOrigin)
            switch (ConversationMessageOrigin.KindOf(message))
            {
                // 交接文档要落盘也要渲染,但渲染成独立卡片而不是助手气泡
                case EHistoryItemKind.HandoffNote:
                    buffer.Add(new HandoffItem
                    {
                        Message = HistoryHandoff.NoteBody(ConversationItemFactory.DisplayTextOf(message)),
                        SourceMessage = message,
                    });
                    continue;

                // 开场白是 assistant 消息(要供给模型,否则首轮又自我介绍一遍),但画成居中旁白
                case EHistoryItemKind.Narration:
                {
                    TextConversationItem narration = _itemActions.Wire(
                        ConversationItemFactory.CreateNarration(message), message);
                    if (lastKnown is { } narrationStamp)
                        narration.Timestamp = ConversationItemFactory.TimestampText(narrationStamp);
                    buffer.Add(narration);
                    continue;
                }

                // 检索片段同样是「落盘但不是对话」:它的角色是 Tool,
                // 落进助手那一档会被当成工具结果去配对一个不存在的调用
                case EHistoryItemKind.Knowledge:
                {
                    ToolCallItem knowledgeCard = ConversationItemFactory.CreateKnowledgeCard(message.Text);
                    knowledgeCard.SourceMessage = message;
                    buffer.Add(knowledgeCard);
                    continue;
                }

                // 子会话的后续报告:角色是 User(它要供给模型),但<b>不是用户说的话</b>——
                // 画成用户气泡等于把子代理的结论安到用户头上。借旁白那套呈现:
                // 居中、无头像无名字,表示"这条不归对话双方任何一方"
                case EHistoryItemKind.SubAgentReport:
                {
                    TextConversationItem reportItem = _itemActions.Wire(
                        ConversationItemFactory.CreateNarration(message), message);
                    if (lastKnown is { } reportStamp)
                        reportItem.Timestamp = ConversationItemFactory.TimestampText(reportStamp);
                    buffer.Add(reportItem);
                    continue;
                }

                // 离席回执：只给人看的一段记录，不归对话双方任何一方
                case EHistoryItemKind.AwayReceipt:
                {
                    TextConversationItem receiptItem = ConversationItemFactory.CreateNarration(message);
                    if (GroupAwayReceipt.Of(message) is { } receipt)
                        receiptItem.Message = GroupAwayReceiptText.Format(receipt);
                    buffer.Add(_itemActions.Wire(receiptItem, message));
                    continue;
                }

                case EHistoryItemKind.UserInput:
                {
                    foreach (TextConversationItem userItem in CreateUserItems(message))
                    {
                        if (lastKnown is { } userStamp)
                            userItem.Timestamp = ConversationItemFactory.TimestampText(userStamp);
                        buffer.Add(userItem);
                    }

                    continue;
                }

                case EHistoryItemKind.StreamContents:
                    break; //落到下面交给转录器按内容装配

                default:
                    // 种类加了一项却没在这里表态。抛出来而不是默默画错:
                    // 静默的重复或缺失查起来要命,而这条路一跑就炸
                    throw new ArgumentOutOfRangeException(nameof(message),
                        $"Unhandled history item kind for message role '{message.Role}'.");
            }

            int before = buffer.Count;
            foreach (AIContent content in message.Contents)
            {
                replay.Apply(content);
            }

            replay.CloseSegment();

            // 本条消息产出的<b>每一个</b>条目都记下来源:删除是按「来源落在删除集合里」
            // 摘条目的,漏记的条目会在来源消失后成为删不掉的残留(思考卡、工具卡都没有
            // 自己的删除按钮)。消息级操作只挂在文本气泡上——只有它有那一行按钮
            for (int i = before; i < buffer.Count; i++)
            {
                buffer[i].SourceMessage = message;
                // 回放定格：命中存档读存档（冻结真耗时），未命中只留字数——
                // 重建的 _startedAt 是打开会话那一刻，不定格就是统一 0.1s 的假耗时
                if (buffer[i] is ThinkingItem thinking) ThinkingItem.FreezeReplayItem(thinking, message);
                if (buffer[i] is not TextConversationItem textItem) continue;

                _itemActions.Wire(textItem, message);
                if (lastKnown is { } stamp) textItem.Timestamp = ConversationItemFactory.TimestampText(stamp);
            }
        }

        // 调用与它的结果是两条消息,开窗分批完全可能把它们切在两批里:不越过批边界找一次,
        // 批尾那次调用就会被下面的收尾误判成「历史里没有这次调用的结果」(liveTail 那一批
        // to 就是历史末尾,这里是空操作)
        replay.ApplyLaterResults(messages, to);

        if (liveTail) replay.CloseSegment();
        else replay.FinalizeReplay(Loc.Text(LangKey.AgentToolCallUnfinished));

        return buffer;
    }

    /// <summary>
    /// 自己那一轮正跑着的时候落的盘：本轮的东西全由实时流渲染过了，这里<b>只补后续报告</b>。
    ///
    /// 它是唯一可能在本轮进行中从别处插进来的一类——子会话交回报告要等派活者空闲
    /// （见 <c>SubAgentReportHandoff</c>），而「登记处已空闲」与「视图的 IsRunning 归零」
    /// 之间有一瞬的错位。整段丢掉的话那条报告就要等重开会话才看得见，用户看到的是「交回丢了」。
    /// 其余几类（检索卡、旁白、交接文档）本轮自有渲染路径，补在这里会画成两条。
    /// </summary>
    private void AppendHandedBackReports(IReadOnlyList<ChatMessage> history, int fromIndex)
    {
        for (int i = fromIndex; i < history.Count; i++)
        {
            if (ConversationMessageOrigin.KindOf(history[i]) != EHistoryItemKind.SubAgentReport) continue;
            Append(history, i, i + 1, liveTail: true);
        }
    }

    /// <summary>没有实时流时：整段照回放渲染</summary>
    private void AppendWholeSlice(IReadOnlyList<ChatMessage> history, int fromIndex)
    {
        AppendSkippingRenderedHandoffs(Build(history, fromIndex, history.Count, liveTail: true));
    }

    /// <summary>
    /// 一轮正往界面流内容时落的盘：<b>内容流产出的那几类已经渲染过了</b>（助手正文、思考段、
    /// 工具卡、被消费的用户消息），这里只补它产不出的（检索卡、旁白、交接文档、后续报告），
    /// 再把流式条目与消息配对。
    /// </summary>
    private void AppendAlongsideStream(IReadOnlyList<ChatMessage> history, int fromIndex)
    {
        for (int i = fromIndex; i < history.Count; i++)
        {
            ChatMessage message = history[i];
            if (ConversationMessageOrigin.IsProducedByContentStream(ConversationMessageOrigin.KindOf(message)))
                continue;

            AppendSkippingRenderedHandoffs(Build(history, i, i + 1, liveTail: true));
        }

        // 流式条目此刻才能与落了盘的消息配对,配上了才有编辑/删除/分叉
        _itemActions.WireStreamed(history);
    }

    /// <summary>
    /// 交接文档可能在落盘渲染入队之前已被别的路径画过（HandoffWritten 兜底、对账追加），
    /// 按来源引用去重——否则同一条 note 会出两张卡
    /// </summary>
    private void AppendSkippingRenderedHandoffs(List<ConversationItemBase> items)
    {
        foreach (ConversationItemBase item in items)
        {
            if (item is HandoffItem && IsRendered(item.SourceMessage)) continue;
            _items.Add(item);
        }
    }

    private bool IsRendered(ChatMessage? source) => _items.Any(x => ReferenceEquals(x.SourceMessage, source));
}
