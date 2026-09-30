using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 群的待审批条（ADR 0046 修订）：成员在群里那一轮要动东西时停下来等，卡摆在群输入框上方。
///
/// 不进群流水：流水记的是大家说了什么，审批不是一句发言；并行时几个人同时等，也能在一条里排开。
/// 待决项取自 <see cref="SessionApprovalRegistry"/>，与成员自己的会话窗口认领的是同一批——哪边先点算哪边
/// </summary>
public sealed class GroupApprovalsViewData : IDisposable
{
    private readonly ChatSession _group;

    /// <summary>此刻在等的审批，按成员、登记顺序</summary>
    public ObservableCollection<GroupApprovalViewData> Items { get; } = new();

    /// <summary>
    /// 盯一个群的待审批
    /// </summary>
    /// <param name="group">群壳会话</param>
    public GroupApprovalsViewData(ChatSession group)
    {
        _group = group;
        SessionApprovalRegistry.Instance.PendingChanged += OnPendingChanged;
        Sync();
    }

    /// <inheritdoc />
    public void Dispose() => SessionApprovalRegistry.Instance.PendingChanged -= OnPendingChanged;

    // 来自后台线程
    private void OnPendingChanged(string sessionId)
    {
        if (_group.GroupMemberSessionIds.Contains(sessionId)) Dispatcher.UIThread.Post(Sync);
    }

    private void Sync()
    {
        List<(string MemberId, ToolApprovalRequestContent Request)> pending = _group.GroupMemberSessionIds
            .SelectMany(id => SessionApprovalRegistry.Instance.PendingOf(id).Select(request => (id, request)))
            .ToList();

        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (!pending.Any(x => ReferenceEquals(x.Request, Items[i].Card.Request))) Items.RemoveAt(i);
        }

        foreach ((string memberId, ToolApprovalRequestContent request) in pending)
        {
            if (Items.Any(x => ReferenceEquals(x.Card.Request, request))) continue;
            if (SessionManager.Instance.Load(memberId) is not { } member) continue;
            Items.Add(new GroupApprovalViewData(member, request));
        }
    }
}

/// <summary>
/// 待审批条里的一条：谁、要做什么，外加同一套审批按钮（卡片逻辑照用 <see cref="ApprovalRequestItem"/>）
/// </summary>
public sealed partial class GroupApprovalViewData
{
    /// <summary>发起审批的成员会话标识</summary>
    public string MemberSessionId { get; }

    /// <summary>成员名</summary>
    public string MemberName { get; }

    /// <summary>审批卡（按钮、同类命令放行、回应都在它身上）</summary>
    public ApprovalRequestItem Card { get; }

    /// <summary>一行摘要：参数摘要的第一行；全文进提示</summary>
    public string Summary { get; }

    /// <summary>
    /// 为一条待决审批造一行并认领它：这一行点出来的决定就是那次审批的回应
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="request">待决的审批请求</param>
    public GroupApprovalViewData(ChatSession member, ToolApprovalRequestContent request)
    {
        MemberSessionId = member.SessionId;
        MemberName = member.CharacterData.CharacterName;
        Card = new ApprovalRequestItem(request, GroupChatSessions.WorkspaceOf(member))
        {
            RememberShellPatternCallback = member.AddSessionApprovedShellPattern,
        };
        string summary = Card.ArgumentSummary.Trim();
        int lineEnd = summary.IndexOf('\n');
        Summary = lineEnd < 0 ? summary : summary[..lineEnd];
        SessionApprovalRegistry.Instance.TryAdopt(member.SessionId, request, Card.Response);
    }

    /// <summary>打开他的会话看完整的卡（diff 等）</summary>
    [RelayCommand]
    private void Open() => SubSessionWindowOpener.Open(MemberSessionId);
}
