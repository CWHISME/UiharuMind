using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 群的右栏成员列表：按发言顺序列出成员，点开是他自己的会话（ADR 0046 决策 1：成员是真会话）。
/// 装载后由 <see cref="MarkSpeaking"/> 跟着调度器的发言人变化刷新，不重建实例
/// </summary>
public sealed class GroupMembersViewData
{
    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话</param>
    public GroupMembersViewData(ChatSession group)
    {
        Members = group.GroupMemberSessionIds
            .Select(id => SessionManager.Instance.GetMeta(id))
            .OfType<ChatSessionMeta>()
            .Select(meta => new GroupMemberItem(meta))
            .ToList();
    }

    /// <summary>成员，顺序即发言顺序</summary>
    public IReadOnlyList<GroupMemberItem> Members { get; }

    /// <summary>
    /// 把「正在说」标到对应成员上；传 null 则全部熄灭（一圈结束或没人开口）。
    /// 同一时刻最多一个为 true，由调度器保证
    /// </summary>
    /// <param name="memberSessionId">正在发言的成员会话；没有为 null</param>
    public void MarkSpeaking(string? memberSessionId)
    {
        foreach (GroupMemberItem member in Members) member.IsSpeaking = member.SessionId == memberSessionId;
    }
}

/// <summary>成员列表里的一项</summary>
public sealed partial class GroupMemberItem : ObservableObject
{
    private readonly CharacterData _character;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="meta">成员会话的元数据</param>
    public GroupMemberItem(ChatSessionMeta meta)
    {
        SessionId = meta.SessionId;
        _character = SessionManager.CharacterOf(meta);
    }

    /// <summary>成员会话标识</summary>
    public string SessionId { get; }

    /// <summary>显示名</summary>
    public string Name => _character.CharacterName;

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(_character);

    /// <summary>此刻是不是群里的发言人（右栏标出正在说的那一位）</summary>
    [ObservableProperty] private bool _isSpeaking;

    /// <summary>打开他自己的会话：与子会话同一个浮窗，骨架里只读</summary>
    [RelayCommand]
    private void Open() => SubSessionWindowOpener.Open(SessionId);
}
