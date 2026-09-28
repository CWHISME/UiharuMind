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

    private readonly List<CharacterData> _picked = []; //勾选顺序即发言顺序
    private readonly List<GroupCandidate> _allCandidates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string _name = string.Empty;

    /// <summary>
    /// 档位筛选。枚举而非下标：下标那版把同一个值抄在常量、属性与 axaml 的 <c>Tag</c> 三处，
    /// 漂移不报错。选项由 <see cref="CharacterFilterPresentation"/> 按枚举派生。
    ///
    /// <b>只有这一条轴</b>：候选集已经是「所有能进群的卡」，量最大、最需要分组，
    /// 但两组胶囊并排会出现两个「全部」，比缺一条轴更别扭。来源那一轴在角色库左栏的
    /// 溢出菜单里（不占版面），挑人时按名字搜定位词也就够定位了。
    /// </summary>
    [ObservableProperty]
    private ECharacterKindFilter _kindFilter;

    /// <summary>搜索词：按名字与描述过滤（口径同 <see cref="CharacterData.MatchesSearch"/>），空则不过滤</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>成员模型的选项：第一项是「跟随全局」，其余来自全局模型列表</summary>
    public ObservableCollection<SessionModelOption> ModelOptions { get; } = [];

    /// <summary>已选成员（顺序即发言顺序）：候选区勾上就进来，每行带着自己的模型下拉</summary>
    public ObservableCollection<GroupCandidate> PickedMembers { get; } = [];

    /// <summary>调度设置（建群时定初值，之后右栏可改）</summary>
    public GroupScheduleViewData Schedule { get; } = new(EGroupScheduleMode.Serial, EGroupStopPolicy.Conservative);

    /// <summary>主持人的选项：第一项是「无」，其余是已选成员（建群时定初值，之后右栏可改）</summary>
    public ObservableCollection<GroupHostOption> HostOptions { get; } = [];

    /// <summary>选着的主持人</summary>
    [ObservableProperty] private GroupHostOption? _selectedHost;

    /// <summary>设计器用</summary>
    public GroupCreateWindowModel() : this(false, null)
    {
    }

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="isAgentGroup">是不是智能体群</param>
    /// <param name="workspacePath">智能体群的工作区；普通群为 null</param>
    /// <param name="preselected">预先勾上的成员（从单聊开群时是原单聊的角色）；没有为 null</param>
    /// <param name="name">预填的群名；没有为 null</param>
    public GroupCreateWindowModel(bool isAgentGroup, string? workspacePath,
        IReadOnlyList<CharacterData>? preselected = null, string? name = null)
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

        // 候选与建群时的校验同源(GroupChatSessions.CanJoin)：两类群都收所有非用户卡——
        // agent 卡进普通群以 chat 形态加入（不带工具，ADR 0050 决策 3）
        SessionModelOption followGlobal = ModelOptions[0];
        _allCandidates = CharacterManager.Instance.CharacterDataDictionary.Values
            // 预选的是用户正在聊的那一位，屏蔽了也照样列出（他本来就看得见），不然预选会静默落空
            .Where(x => !x.IsInternal && GroupChatSessions.CanJoin(x)
                        && (CharacterVisibility.PassesShield(x) || preselected?.Any(p => p.CharacterId == x.CharacterId) == true))
            .OrderBy(x => x.IsAgent)
            .ThenBy(x => x.CharacterName, StringComparer.CurrentCulture)
            .Select(x => new GroupCandidate(x, OnCandidateToggled, ModelOptions)
            {
                SelectedModelOption = followGlobal,
            })
            .ToList();
        HostOptions.Add(GroupHostOption.None);
        SelectedHost = GroupHostOption.None;
        ApplyFilter();

        Name = name ?? string.Empty;
        foreach (CharacterData member in preselected ?? [])
        {
            if (_allCandidates.FirstOrDefault(x => x.Data.CharacterId == member.CharacterId) is { } candidate)
                candidate.IsPicked = true;
        }
    }

    /// <summary>是不是智能体群</summary>
    public bool IsAgentGroup { get; }

    /// <summary>类型说明：智能体群绑哪个工作区；普通群不绑，成员一律以普通对话形态参与</summary>
    public string TypeHint { get; }

    /// <summary>当前筛出来的候选（档位 + 搜索词），勾选与建群仍走全量校验</summary>
    public ObservableCollection<GroupCandidate> Candidates { get; } = [];

    /// <summary>筛空了：列表区显示空提示（同 CharacterPickerView 的 IsEmpty 口径）</summary>
    public bool IsEmpty => Candidates.Count == 0;

    /// <summary>档位轴的胶囊。选中态是快照，切档后重建这一组</summary>
    public IReadOnlyList<CharacterFilterPill> KindPills =>
        CharacterFilterPills.Build(CharacterFilterPresentation.KindOptions(), KindFilter,
            value => KindFilter = value);

    /// <summary>已选成员，顺序即发言顺序</summary>
    public IReadOnlyList<CharacterData> Picked => _picked;

    /// <summary>已选成员的模型名，顺序与 <see cref="Picked"/> 一致；null = 跟随全局</summary>
    public IReadOnlyList<string?> PickedModelNames =>
        PickedMembers.Select(x => x.SelectedModelOption?.ModelName).ToList();

    /// <summary>发言顺序的一行字</summary>
    public string SpeakingOrder => _picked.Count == 0
        ? Loc.Text(LangKey.GroupCreateOrderEmpty)
        : string.Join(" → ", _picked.Select(x => x.CharacterName));

    /// <summary>建群时的调度设置：主持人按它在已选成员里的位置记</summary>
    public GroupSchedule PickedSchedule => new(Schedule.Mode, Schedule.StopPolicy,
        SelectedHost?.Data is { } host ? _picked.IndexOf(host) : -1);

    /// <summary>名字不空、至少两位成员</summary>
    public bool CanCreate => !string.IsNullOrWhiteSpace(Name) && _picked.Count >= MinMembers;

    private void OnCandidateToggled(GroupCandidate candidate, bool picked)
    {
        _picked.Remove(candidate.Data);
        if (picked) _picked.Add(candidate.Data);
        if (picked && !PickedMembers.Contains(candidate)) PickedMembers.Add(candidate);
        else if (!picked) PickedMembers.Remove(candidate);
        SyncHostOptions();
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(SpeakingOrder));
    }

    // 主持人只能从已选成员里挑：取消勾选的人正好是主持人，就退回「无」
    private void SyncHostOptions()
    {
        GroupHostOption? selected = SelectedHost;
        while (HostOptions.Count > 1) HostOptions.RemoveAt(HostOptions.Count - 1);
        foreach (CharacterData member in _picked) HostOptions.Add(new GroupHostOption(member));
        SelectedHost = HostOptions.FirstOrDefault(x => x.Data != null && x.Data == selected?.Data) ?? GroupHostOption.None;
    }

    partial void OnKindFilterChanged(ECharacterKindFilter value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(KindPills));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Candidates.Clear();
        foreach (GroupCandidate candidate in _allCandidates)
        {
            bool kindMatches = KindFilter switch
            {
                ECharacterKindFilter.All => true,
                ECharacterKindFilter.Chat => !candidate.Data.IsAgent,
                ECharacterKindFilter.Agent => candidate.Data.IsAgent,
                _ => true,
            };
            if (kindMatches && candidate.Data.MatchesSearch(SearchText)) Candidates.Add(candidate);
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

/// <summary>主持人下拉里的一项</summary>
/// <param name="Data">成员角色；null 为「无主持人」</param>
public sealed record GroupHostOption(CharacterData? Data)
{
    /// <summary>无主持人</summary>
    public static GroupHostOption None { get; } = new((CharacterData?)null);

    /// <summary>显示名</summary>
    public string Name => Data?.CharacterName ?? Loc.Text(LangKey.GroupHostNone);
}

/// <summary>建群请求</summary>
/// <param name="Name">群名</param>
/// <param name="Members">成员，顺序即发言顺序</param>
/// <param name="MemberModelNames">成员各自的模型名，顺序与 <paramref name="Members"/> 一致；null = 跟随全局</param>
/// <param name="Schedule">调度设置</param>
public sealed record GroupCreateRequest(string Name, IReadOnlyList<CharacterData> Members,
    IReadOnlyList<string?> MemberModelNames, GroupSchedule Schedule);
