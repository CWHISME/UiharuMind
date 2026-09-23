using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Characters;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 建群弹窗的数据。群的类型由调用方给定（跟着切换器那一侧），这里只挑名字与成员；
/// <b>勾选顺序即发言顺序</b>，所以记的是一个有序名单而不是一组勾。
/// </summary>
public partial class GroupCreateWindowModel : ObservableObject
{
    private const int MinMembers = 2; //一个人的群就是单聊

    private readonly List<CharacterData> _picked = []; //勾选顺序即发言顺序

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string _name = string.Empty;

    /// <summary>设计器用</summary>
    public GroupCreateWindowModel() : this(false, null)
    {
    }

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="isAgentGroup">是不是智能体群</param>
    /// <param name="workspacePath">智能体群的工作区；普通群为 null</param>
    public GroupCreateWindowModel(bool isAgentGroup, string? workspacePath)
    {
        IsAgentGroup = isAgentGroup;
        TypeHint = isAgentGroup
            ? Loc.Text(LangKey.GroupCreateAgentHint,
                string.IsNullOrEmpty(workspacePath) ? Loc.Text(LangKey.GroupCreateNoWorkspace) : workspacePath)
            : Loc.Text(LangKey.GroupCreateChatHint);
        // 候选与建群时的校验同源(GroupChatSessions.CanJoin):普通群里不会出现智能体
        Candidates = CharacterManager.Instance.CharacterDataDictionary.Values
            .Where(x => !x.IsInternal && GroupChatSessions.CanJoin(x, isAgentGroup))
            .OrderBy(x => x.IsAgent)
            .ThenBy(x => x.CharacterName, StringComparer.CurrentCulture)
            .Select(x => new GroupCandidate(x, OnCandidateToggled))
            .ToList();
    }

    /// <summary>是不是智能体群</summary>
    public bool IsAgentGroup { get; }

    /// <summary>类型说明：智能体群绑哪个工作区，普通群只收普通角色</summary>
    public string TypeHint { get; }

    /// <summary>能进这类群的角色</summary>
    public IReadOnlyList<GroupCandidate> Candidates { get; }

    /// <summary>已选成员，顺序即发言顺序</summary>
    public IReadOnlyList<CharacterData> Picked => _picked;

    /// <summary>发言顺序的一行字</summary>
    public string SpeakingOrder => _picked.Count == 0
        ? Loc.Text(LangKey.GroupCreateOrderEmpty)
        : string.Join(" → ", _picked.Select(x => x.CharacterName));

    /// <summary>名字不空、至少两位成员</summary>
    public bool CanCreate => !string.IsNullOrWhiteSpace(Name) && _picked.Count >= MinMembers;

    private void OnCandidateToggled(GroupCandidate candidate, bool picked)
    {
        _picked.Remove(candidate.Data);
        if (picked) _picked.Add(candidate.Data);
        OnPropertyChanged(nameof(SpeakingOrder));
        OnPropertyChanged(nameof(CanCreate));
    }
}

/// <summary>建群弹窗里的一个候选角色</summary>
public partial class GroupCandidate : ObservableObject
{
    private readonly Action<GroupCandidate, bool> _toggled;

    [ObservableProperty] private bool _isPicked;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="data">角色</param>
    /// <param name="toggled">勾选变化时回报给弹窗（它要按勾选顺序排发言）</param>
    public GroupCandidate(CharacterData data, Action<GroupCandidate, bool> toggled)
    {
        Data = data;
        _toggled = toggled;
    }

    /// <summary>角色</summary>
    public CharacterData Data { get; }

    /// <summary>显示名</summary>
    public string Name => Data.CharacterName;

    /// <summary>类别显示名（普通角色 / 智能体）</summary>
    public string KindName => CharacterKindPresentation.NameOf(Data);

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(Data);

    partial void OnIsPickedChanged(bool value) => _toggled(this, value);
}

/// <summary>建群请求</summary>
/// <param name="Name">群名</param>
/// <param name="Members">成员，顺序即发言顺序</param>
public sealed record GroupCreateRequest(string Name, IReadOnlyList<CharacterData> Members);
