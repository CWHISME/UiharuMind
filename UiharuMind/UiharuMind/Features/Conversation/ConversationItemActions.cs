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
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Chat;
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
    /// <param name="input">用户消息</param>
    void Rerun(ChatMessage input);

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

    /// <param name="items">界面条目集合(与视图模型共用同一个实例)</param>
    /// <param name="host">要回头请视图模型做的那几件事</param>
    public ConversationItemActions(ObservableCollection<ConversationItemBase> items,
        IConversationItemActionHost host)
    {
        _items = items;
        _host = host;
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
        // 点名调用的气泡显示的是 /技能名 那一行,而消息正文是注入的技能全文;
        // 放开编辑会把正文改写成那一行,当场毁掉注入内容
        if (NamedSkillAnnotations.InputOf(source) == null) item.EditedCallback = OnEdited;
        item.DeleteCallback = OnDeleted;
        item.BranchCallback = OnBranch;
        // 重试语义是"从这条用户输入起重新生成",因此只挂在用户消息上
        if (source.Role == ChatRole.User) item.RetryCallback = Retry;
        return item;
    }

    /// <summary>
    /// 一轮结束后，历史已由提供器写入（本轮输入 + 回复）。
    /// 把界面上刚产出的、还没有来源消息的文本气泡按角色与历史尾部配对，使其也能被操作。
    /// </summary>
    /// <param name="history">当前历史</param>
    public void WireStreamed(IReadOnlyList<ChatMessage> history)
    {
        int cursor = history.Count - 1;

        for (int i = _items.Count - 1; i >= 0 && cursor >= 0; i--)
        {
            if (_items[i] is not TextConversationItem item) continue;
            if (item.SourceMessage != null) break; //再往前都是回放来的,已经关联过

            // 只在角色一致时配对,不一致说明界面与历史的形状对不上,宁可不提供操作
            ChatRole expected = item.IsUser ? ChatRole.User : ChatRole.Assistant;
            while (cursor >= 0 && history[cursor].Role != expected) cursor--;
            if (cursor < 0) break;

            Wire(item, history[cursor]);
            cursor--;
        }

        _host.NotifyItemsWired();
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

    private void OnDeleted(ConversationItemBase item)
    {
        ChatSession? session = _host.Session;
        if (session != null && item.SourceMessage != null)
        {
            session.History.Remove(item.SourceMessage);
            session.Save();
        }

        _items.Remove(item);
    }

    private void OnBranch(ConversationItemBase item)
    {
        ChatSession? session = _host.Session;
        if (session == null || item.SourceMessage == null) return;

        int index = session.History.IndexOf(item.SourceMessage);
        if (index < 0) return;

        ChatSession branch = SessionManager.Instance.DeepCopy(session);
        branch.SessionId = Guid.NewGuid().ToString("N");
        branch.Title = $"{session.Title} {LocalizationManager.Instance.GetString("ChatBranchSuffix")}";
        branch.CreatedAt = DateTimeOffset.Now;
        // 附件文件仍归原会话所有:两边都登记会导致删除任一方时打断另一方
        branch.OwnedAttachmentFiles.Clear();
        // 保留到该条消息为止
        branch.History.RemoveRange(index + 1, branch.History.Count - index - 1);
        SessionManager.Instance.Add(branch);
        _host.NotifySessionsChanged();
    }

    /// <summary>
    /// 从某条用户输入起重新生成。也是「重新生成上一条」那个命令的落点
    /// </summary>
    /// <param name="item">用户条目</param>
    public void Retry(ConversationItemBase item)
    {
        ChatSession? session = _host.Session;
        if (session == null || item.SourceMessage == null || _host.IsGenerating) return;

        int index = session.History.IndexOf(item.SourceMessage);
        if (index < 0) return;

        // 丢弃该条用户输入之后的全部历史,再以它为输入重跑一轮
        ChatMessage input = session.History[index];
        session.History.RemoveRange(index, session.History.Count - index);
        session.Save();

        int itemIndex = _items.IndexOf(item);
        if (itemIndex >= 0)
        {
            for (int i = _items.Count - 1; i >= itemIndex; i--) _items.RemoveAt(i);
        }

        _items.Add(Wire(ConversationItemFactory.CreateUser(
            ConversationItemFactory.DisplayTextOf(input), input), input));
        _host.Rerun(input);
    }
}
