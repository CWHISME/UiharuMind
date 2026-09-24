using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Core;
using UiharuMind.Features.Characters;
using UiharuMind.Features.Conversation.SidePanels;
using UiharuMind.Features.Models;
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

    /// <summary>成员模型的选项：第一项是「跟随全局」，其余来自全局模型列表</summary>
    public ObservableCollection<SessionModelOption> ModelOptions { get; } = [];

    /// <summary>已选成员（顺序即发言顺序）：候选区勾上就进来，每行带着自己的模型下拉</summary>
    public ObservableCollection<GroupCandidate> PickedMembers { get; } = [];

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
        // 成员模型的选项：默认「跟随全局」+ 当前模型清单。模型清单打开窗口时拍一张快照即可
        // （窗口是模态的短命界面，模型列表在这几秒里变了的概率和代价都不值得挂订阅）
        ModelOptions.Add(new SessionModelOption
        {
            IsDefault = true,
            DisplayName = Loc.Text(LangKey.GroupModelFollowGlobal),
        });
        if (App.ModelService?.ModelSources is { } sources)
        {
            foreach (ModelRunningData model in sources)
            {
                ModelOptions.Add(new SessionModelOption
                {
                    ModelName = model.ModelName,
                    DisplayName = model.ModelName,
                    IsRemoteModel = model.IsRemoteModel,
                    IsVisionModel = model.IsVisionModel,
                });
            }
        }

        // 候选与建群时的校验同源(GroupChatSessions.CanJoin):普通群里不会出现智能体
        SessionModelOption followGlobal = ModelOptions[0];
        _allCandidates = CharacterManager.Instance.CharacterDataDictionary.Values
            .Where(x => !x.IsInternal && CharacterVisibility.PassesShield(x) && GroupChatSessions.CanJoin(x, isAgentGroup))
            .OrderBy(x => x.IsAgent)
            .ThenBy(x => x.CharacterName, StringComparer.CurrentCulture)
            .Select(x => new GroupCandidate(x, OnCandidateToggled, ModelOptions)
            {
                SelectedModelOption = followGlobal,
            })
            .ToList();
        ApplyFilter();
    }

    /// <summary>是不是智能体群</summary>
    public bool IsAgentGroup { get; }

    /// <summary>类型说明：智能体群绑哪个工作区，普通群只收普通角色</summary>
    public string TypeHint { get; }

    /// <summary>当前筛出来的候选（类别筛选 + 搜索词），勾选与建群仍走全量校验</summary>
    public ObservableCollection<GroupCandidate> Candidates { get; } = [];

    /// <summary>筛空了：列表区显示空提示（同 CharacterPickerView 的 IsEmpty 口径）</summary>
    public bool IsEmpty => Candidates.Count == 0;

    /// <summary>类别筛选：全部</summary>
    public bool IsKindFilterAll => KindFilterIndex == KindFilterAll;

    /// <summary>类别筛选：普通角色</summary>
    public bool IsKindFilterChat => KindFilterIndex == KindFilterChat;

    /// <summary>类别筛选：智能体</summary>
    public bool IsKindFilterAgent => KindFilterIndex == KindFilterAgent;

    /// <summary>已选成员，顺序即发言顺序</summary>
    public IReadOnlyList<CharacterData> Picked => _picked;

    /// <summary>已选成员的模型名，顺序与 <see cref="Picked"/> 一致；null = 跟随全局</summary>
    public IReadOnlyList<string?> PickedModelNames =>
        PickedMembers.Select(x => x.SelectedModelOption?.ModelName).ToList();

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
        if (picked && !PickedMembers.Contains(candidate)) PickedMembers.Add(candidate);
        else if (!picked) PickedMembers.Remove(candidate);
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(SpeakingOrder));
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
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>建群弹窗里的一个候选角色</summary>
public partial class GroupCandidate : ObservableObject
{
    private readonly Action<GroupCandidate, bool> _toggled;

    [ObservableProperty] private bool _isPicked;

    [ObservableProperty] private SessionModelOption? _selectedModelOption;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="data">角色</param>
    /// <param name="toggled">勾选变化时回报给弹窗（它要按勾选顺序排发言）</param>
    /// <param name="modelOptions">成员模型的选项（全窗口共享同一份）</param>
    public GroupCandidate(CharacterData data, Action<GroupCandidate, bool> toggled,
        IReadOnlyList<SessionModelOption> modelOptions)
    {
        Data = data;
        _toggled = toggled;
        ModelOptions = modelOptions;
    }

    /// <summary>角色</summary>
    public CharacterData Data { get; }

    /// <summary>成员模型的选项（共享窗口级列表，第一项是「跟随全局」）</summary>
    public IReadOnlyList<SessionModelOption> ModelOptions { get; }

    /// <summary>显示名</summary>
    public string Name => Data.CharacterName;

    /// <summary>角色描述（候选行第二行）</summary>
    public string Description => Data.Description;

    /// <summary>有没有可显示的描述：空描述不占行</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>类别显示名（普通角色 / 智能体）</summary>
    public string KindName => CharacterKindPresentation.NameOf(Data);

    /// <summary>类别徽章底色</summary>
    public IBrush KindColor => CharacterKindPresentation.BrushOf(Data);

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(Data);

    partial void OnIsPickedChanged(bool value) => _toggled(this, value);
}

/// <summary>建群请求</summary>
/// <param name="Name">群名</param>
/// <param name="Members">成员，顺序即发言顺序</param>
/// <param name="MemberModelNames">成员各自的模型名，顺序与 <paramref name="Members"/> 一致；null = 跟随全局</param>
public sealed record GroupCreateRequest(string Name, IReadOnlyList<CharacterData> Members,
    IReadOnlyList<string?> MemberModelNames);
