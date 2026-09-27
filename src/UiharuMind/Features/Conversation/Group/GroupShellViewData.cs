using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Composer;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 群壳会话（ADR 0046）才有的那一份：右栏群卡、成员列表、产物区、待审批条，以及发言/继续/停止这几件
/// 交给调度器的事。对话视图模型只在打开的是群壳时持有它（否则为 null），群相关的判断因此收成一次判空。
/// 群壳自己不跑轮，所以这里不碰执行者
/// </summary>
public sealed class GroupShellViewData : IDisposable
{
    private readonly ChatSession _group;

    /// <summary>
    /// 构造并挂上发言人变化的通知。装载前这一圈可能已经在跑、信号早发完了，所以构造时先补标一次
    /// </summary>
    /// <param name="group">群壳会话</param>
    public GroupShellViewData(ChatSession group)
    {
        _group = group;
        Members = new GroupMembersViewData(group);
        Approvals = new GroupApprovalsViewData(group);
        Artifacts = new GroupArtifactsViewData(group);
        Members.MarkSpeaking(GroupChatCoordinator.Instance.SpeakersOf(group.SessionId));
        Members.RefreshRunStates();
        // 发言人变化（轮到谁 / 一轮结束）在后台线程上跑，处理里自行 marshal
        GroupChatCoordinator.Instance.SpeakerChanged += OnSpeakerChanged;
    }

    /// <summary>发言人变了（已在 UI 线程上）：忙碌文案要跟着换</summary>
    public event Action? SpeakersChanged;

    /// <summary>右栏成员列表</summary>
    public GroupMembersViewData Members { get; }

    /// <summary>输入区上方的待审批条</summary>
    public GroupApprovalsViewData Approvals { get; }

    /// <summary>右栏产物区</summary>
    public GroupArtifactsViewData Artifacts { get; }

    /// <summary>群名（右栏群卡）</summary>
    public string Title => _group.Title;

    /// <summary>群描述（成员名单，右栏群卡）</summary>
    public string Description => _group.Description;

    /// <summary>群的类型显示名（右栏群卡）</summary>
    public string TypeName => Loc.Text(_group.IsAgentGroup ? LangKey.GroupTypeAgent : LangKey.GroupTypeChat);

    /// <summary>成员数显示文本（右栏群卡）</summary>
    public string MemberCountText => string.Format(Loc.Text(LangKey.GroupMemberCountFormat), Members.Members.Count);

    /// <summary>@ 补全的候选：群里的成员</summary>
    public IEnumerable<MentionTarget> MentionTargets =>
        Members.Members.Select(x => new MentionTarget(x.Name, x.Description, x.Icon));

    /// <summary>
    /// 忙碌文案：谁在发言。空窗（还没轮到任何人）时退到泛化的一句，别让转圈旁边一个字都没有
    /// </summary>
    /// <returns>文案；这一圈没在跑为 null</returns>
    public string? BusyLabel()
    {
        if (!GroupChatCoordinator.Instance.IsRunning(_group.SessionId)) return null;
        string speakers = CurrentSpeakerNames();
        return speakers.Length > 0
            ? string.Format(Loc.Text(LangKey.GroupSpeakingNowFormat), speakers)
            : Loc.Text(LangKey.GroupRoundRunning);
    }

    /// <summary>
    /// 用户在群里发言：闲着就开一圈，跑着就插进当前发言人那一轮
    /// </summary>
    /// <param name="text">发言正文</param>
    /// <param name="images">随发的图</param>
    public Task PostAsync(string text, IReadOnlyList<DataContent>? images) =>
        GroupChatCoordinator.Instance.PostAsync(_group, text, images);

    /// <summary>
    /// 不开口也让大家接着说：串行再说一圈（ADR 0046 决策 5），并行叫醒还有新话没听的人（ADR 0049 决策 6 的手动兜底）
    /// </summary>
    public Task ContinueAsync() => GroupChatCoordinator.Instance.ContinueAsync(_group);

    /// <summary>停掉这一圈：当前发言人与后面还没轮到的人一起停</summary>
    public void Stop() => GroupChatCoordinator.Instance.Stop(_group.SessionId);

    /// <summary>
    /// 某个会话的运行态变了。是本群成员就让右栏那一行跟着标（开跑、卡上 / 放开审批、跑完）
    /// </summary>
    /// <param name="sessionId">状态变化的会话</param>
    public void OnSessionRunStateChanged(string sessionId)
    {
        if (Members.Contains(sessionId)) Dispatcher.UIThread.Post(Members.RefreshRunStates);
    }

    /// <summary>
    /// 某个会话刚报了一次用量（可能来自执行线程）。是本群成员就刷右栏那一行
    /// </summary>
    /// <param name="sessionId">报用量的会话</param>
    public void OnSessionUsageReported(string sessionId)
    {
        if (Members.Contains(sessionId)) Dispatcher.UIThread.Post(() => Members.RefreshUsageOf(sessionId));
    }

    /// <summary>摘掉订阅并弃用待审批条与产物区</summary>
    public void Dispose()
    {
        GroupChatCoordinator.Instance.SpeakerChanged -= OnSpeakerChanged;
        Approvals.Dispose();
        Artifacts.Dispose();
    }

    private void OnSpeakerChanged(string groupId)
    {
        if (groupId != _group.SessionId) return;
        Dispatcher.UIThread.Post(() =>
        {
            Members.MarkSpeaking(GroupChatCoordinator.Instance.SpeakersOf(groupId));
            SpeakersChanged?.Invoke();
        });
    }

    /// <summary>此刻正在发言的成员名（并行时可能几位）；没有（空窗 / 成员已删）为空串</summary>
    private string CurrentSpeakerNames()
    {
        IEnumerable<string> names = GroupChatCoordinator.Instance.SpeakersOf(_group.SessionId)
            .Select(id => SessionManager.Instance.GetMeta(id))
            .OfType<ChatSessionMeta>()
            .Select(meta => SessionManager.CharacterOf(meta).CharacterName);
        return string.Join(Loc.Text(LangKey.GroupSpeakerSeparator), names);
    }
}
