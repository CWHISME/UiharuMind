using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
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

    private const int KindFilterAll = 0; //类别筛选：全部
    private const int KindFilterChat = 1; //类别筛选：普通角色
    private const int KindFilterAgent = 2; //类别筛选：智能体

    private readonly List<CharacterData> _picked = []; //勾选顺序即发言顺序
    private readonly List<GroupCandidate> _allCandidates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string _name = string.Empty;

    /// <summary>类别筛选下标（全部/普通角色/智能体），变了就重筛列表</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKindFilterAll))]
    [NotifyPropertyChangedFor(nameof(IsKindFilterChat))]
    [NotifyPropertyChangedFor(nameof(IsKindFilterAgent))]
    private int _kindFilterIndex;

    /// <summary>搜索词：按名字过滤，空则不过滤</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

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
        _allCandidates = CharacterManager.Instance.CharacterDataDictionary.Values
            .Where(x => !x.IsInternal && CharacterVisibility.PassesShield(x) && GroupChatSessions.CanJoin(x, isAgentGroup))
            .OrderBy(x => x.IsAgent)
            .ThenBy(x => x.CharacterName, StringComparer.CurrentCulture)
            .Select(x => new GroupCandidate(x, OnCandidateToggled))
            .ToList();
        ApplyFilter();
    }

    /// <summary>是不是智能体群</summary>
    public bool IsAgentGroup { get; }

    /// <summary>类型说明：智能体群绑哪个工作区，普通群只收普通角色</summary>
    public string TypeHint { get; }

    /// <summary>当前筛出来的候选（类别筛选 + 搜索词），勾选与建群仍走全量校验</summary>
    public ObservableCollection<GroupCandidate> Candidates { get; } = [];

    /// <summary>类别筛选：全部</summary>
    public bool IsKindFilterAll => KindFilterIndex == KindFilterAll;

    /// <summary>类别筛选：普通角色</summary>
    public bool IsKindFilterChat => KindFilterIndex == KindFilterChat;

    /// <summary>类别筛选：智能体</summary>
    public bool IsKindFilterAgent => KindFilterIndex == KindFilterAgent;

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

    partial void OnKindFilterIndexChanged(int value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        string keyword = SearchText.Trim();
        Candidates.Clear();
        foreach (GroupCandidate c in _allCandidates)
        {
            bool kindMatches = KindFilterIndex == KindFilterAll
                || (KindFilterIndex == KindFilterChat && !c.Data.IsAgent)
                || (KindFilterIndex == KindFilterAgent && c.Data.IsAgent);
            bool nameMatches = keyword.Length == 0
                || c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            if (kindMatches && nameMatches) Candidates.Add(c);
        }
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

    /// <summary>是不是智能体（徽章底色由 KindBadge 按它自己选）</summary>
    public bool IsAgent => Data.IsAgent;

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(Data);

    partial void OnIsPickedChanged(bool value) => _toggled(this, value);
}

/// <summary>建群请求</summary>
/// <param name="Name">群名</param>
/// <param name="Members">成员，顺序即发言顺序</param>
public sealed record GroupCreateRequest(string Name, IReadOnlyList<CharacterData> Members);
