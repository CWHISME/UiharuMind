using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 群成员会话（打开的是某位成员自己的会话）才有的那一份：群里广播进来还没接收的发言、群投递的拆段渲染、
/// 权限档跟群走、用户直接打的字是私聊。对话视图模型只在打开的是成员会话时持有它（否则为 null）
/// </summary>
public sealed partial class GroupMemberSessionViewData : ObservableObject, IDisposable
{
    private readonly ChatSession _session;
    private readonly ICharacterRunner _runner; //盯着待发插话的那个执行者（群广播不经视图插进来，得问它）
    private readonly Action<int> _applyPermission;
    private GroupDeliveryRenderer? _deliveryRenderer; //成员名单建群后不变，缓存一份

    [ObservableProperty] private string _pendingText = string.Empty; //群里广播进来、他还没接收的：合成一行
    [ObservableProperty] private string _pendingTip = string.Empty; //那几条的全文

    /// <summary>
    /// 构造并挂上待发插话与群权限档的通知
    /// </summary>
    /// <param name="session">成员会话</param>
    /// <param name="applyPermission">群改了权限档时，把成员会话上的新档交给视图显示（已在 UI 线程上）</param>
    public GroupMemberSessionViewData(ChatSession session, Action<int> applyPermission)
    {
        _session = session;
        _applyPermission = applyPermission;
        _runner = session.Runner;
        _runner.PendingInjectionsChanged += OnPendingInjectionsChanged;
        GroupChatSessions.PermissionChanged += OnPermissionChanged;
        OnPendingInjectionsChanged(); //挂上来时可能已经有话在排队
    }

    /// <summary>有没有群里广播进来、他还没接收的发言（输入区上方那一行据此显隐）</summary>
    public bool HasPending => PendingText.Length > 0;

    /// <summary>群投递的渲染器：把一条投递拆成各发言人的气泡；群已不在为 null</summary>
    public GroupDeliveryRenderer? DeliveryRenderer => _deliveryRenderer ??= GroupDeliveryRenderer.For(_session);

    /// <summary>
    /// 他正在跑<b>群里那一轮</b>（不是本地私聊轮）。判据取运行态登记处：群轮跑成员时把成员会话登记为 busy，
    /// 而视图自己的那一轮闲着。此时打字不该走插话——插话的回应会被群轮按「这一轮正文」收成群发言，
    /// 改走正常发送：过闸时叫停群轮（ADR 0063）
    /// </summary>
    /// <param name="ownTurnRunning">视图自己驱动的那一轮是否在跑</param>
    /// <returns>是否正跑着群轮</returns>
    public bool IsRunningGroupTurn(bool ownTurnRunning) =>
        !ownTurnRunning && SessionManager.Instance.Running.IsBusy(_session.SessionId);

    /// <summary>
    /// 权限档的悬停提示：注明跟群走
    /// </summary>
    /// <param name="permissionTooltip">档位本身的提示</param>
    /// <returns>带跟群说明的提示</returns>
    public static string PermissionTooltip(string permissionTooltip) =>
        string.Format(Loc.Text(LangKey.GroupMemberPermissionFollowFormat), permissionTooltip);

    /// <summary>
    /// 组装用户直接打的话：这是私聊，正文前带一句私聊说明交给模型（不然它分不清这句是私聊还是群里说的，
    /// 照群聊口吻回），界面照样显示原话
    /// </summary>
    /// <param name="text">用户原话</param>
    /// <param name="build">按正文组装用户消息（附件由它带上）</param>
    /// <returns>标过私聊的用户消息</returns>
    public static ChatMessage BuildPrivateMessage(string text, Func<string, ChatMessage> build)
    {
        ChatMessage message = build(GroupTranscript.WithPrivateNote(text));
        ChatMessageAnnotations.MarkGroupPrivate(message);
        return message;
    }

    /// <summary>摘掉订阅。会话比视图活得久，不摘就是一路泄漏到已销毁的视图上</summary>
    public void Dispose()
    {
        _runner.PendingInjectionsChanged -= OnPendingInjectionsChanged;
        GroupChatSessions.PermissionChanged -= OnPermissionChanged;
    }

    partial void OnPendingTextChanged(string value) => OnPropertyChanged(nameof(HasPending));

    /// <summary>执行者的待发插话变了（可能在后台线程上）：切回 UI 线程再对一遍</summary>
    private void OnPendingInjectionsChanged() => Dispatcher.UIThread.Post(SyncPending);

    /// <summary>
    /// 群广播进来、他还没接收的发言合成<b>一行</b>（条数 + 最新一条，全文进提示）：
    /// 群轮不经视图插话，不显示的话他正说着的时候，群里的新话在他会话里一点影子都没有；
    /// 逐条列又会在插话多时把会话区挤没。被消费或被群轮撤回时执行者都会通知，这里跟着对掉
    /// </summary>
    private void SyncPending()
    {
        List<string> posts = _session.Runner.PendingInjections
            .Where(ChatMessageAnnotations.IsGroupDelivery)
            .Select(PendingPostText)
            .ToList();

        PendingText = posts.Count == 0
            ? string.Empty
            : string.Format(Loc.Text(LangKey.GroupInjectionPendingFormat), posts.Count, posts[^1]);
        PendingTip = string.Join("\n", posts);
    }

    private static string PendingPostText(ChatMessage message)
    {
        GroupDeliverySegment post = GroupTranscript.ParsePost(message.Text);
        return post.Speaker == null ? post.Body : $"{post.Speaker}：{post.Body}";
    }

    /// <summary>
    /// 群改了权限档：是本群就按群的新档刷新显示。只刷显示、不写回（成员跟群走、不存副本）
    /// </summary>
    private void OnPermissionChanged(string groupId)
    {
        if (groupId != _session.GroupId) return;
        Dispatcher.UIThread.Post(() => _applyPermission(GroupChatSessions.PermissionOf(_session)));
    }
}
