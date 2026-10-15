using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// 挑群成员：候选（筛选 + 搜索）、已选（<b>勾选顺序即发言顺序</b>）与各自的模型。建群与之后加人共用
/// </summary>
public partial class GroupCandidatePicker : ObservableObject
{
    private readonly List<CharacterData> _picked = []; //勾选顺序即发言顺序
    private readonly List<GroupCandidate> _allCandidates;
    private readonly bool _agentsFirst; //智能体群的挑人场景：智能体在前；普通群保持普通在前

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

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="alwaysListed">屏蔽了也照样列出的角色（用户正在跟他聊，本来就看得见）；没有为 null</param>
    /// <param name="excludedIds">不列的角色标识（已在群里的）；没有为 null</param>
    /// <param name="formerIds">曾在群里、已退出的角色标识（候选行挂徽章）；没有为 null</param>
    public GroupCandidatePicker(IReadOnlyList<CharacterData>? alwaysListed = null,
        IReadOnlySet<string>? excludedIds = null, IReadOnlySet<string>? formerIds = null, bool agentsFirst = false)
    {
        // 成员模型的选项：默认「跟随全局」+ 当前模型清单，打开窗口时拍一张快照
        foreach (SessionModelOption option in SessionModelOption.SnapshotWithDefault(Loc.Text(LangKey.GroupModelFollowGlobal)))
            ModelOptions.Add(option);

        // 候选与建群时的校验同源(GroupChatSessions.CanJoin)：两类群都收所有非用户卡——
        // agent 卡进普通群以 chat 形态加入（不带工具，ADR 0050 决策 3）
        SessionModelOption followGlobal = ModelOptions[0];
        _agentsFirst = agentsFirst;
        _allCandidates = CharacterManager.Instance.CharacterDataDictionary.Values
            .Where(x => !x.IsInternal && GroupChatSessions.CanJoin(x)
                        && excludedIds?.Contains(x.CharacterId) != true
                        && (CharacterVisibility.PassesShield(x) || alwaysListed?.Any(p => p.CharacterId == x.CharacterId) == true))
            .Select(x => new GroupCandidate(x, OnCandidateToggled, OnCandidateFavoriteToggled, ModelOptions)
            {
                SelectedModelOption = followGlobal,
                WasMember = formerIds?.Contains(x.CharacterId) == true,
            })
            .ToList();
        _allCandidates.Sort(CompareCandidates);
        ApplyFilter();
    }

    /// <summary>
    /// 用建群面板的成员模型记忆（<see cref="GroupModelMemory"/>）预填候选行的模型下拉：
    /// 有记忆的成员直接带出上次的模型，没记忆的保持「跟随全局」。
    /// 记忆里的模型不在当前清单（已删/改名）时落回跟随全局，不显示缺失条。
    /// <para>刻意只供建群调用（<c>GroupCreateWindowModel</c>）；加人那条有自己的恢复逻辑，不预填</para>
    /// </summary>
    /// <param name="preferredModelOf">成员角色 id → 记忆模型名；无记忆返回 null</param>
    public void PrefillFrom(Func<string, string?> preferredModelOf)
    {
        foreach (GroupCandidate candidate in _allCandidates)
        {
            string? modelName = preferredModelOf(candidate.Data.CharacterId);
            if (modelName == null) continue;
            if (ModelOptions.FirstOrDefault(x => x.ModelName == modelName) is { } option)
                candidate.SelectedModelOption = option;
        }
    }

    /// <summary>
    /// 候选排序：收藏恒置顶（显式的常用标记，不看群类型），然后是档位轴——
    /// 智能体群挑人以智能体为主（<see cref="_agentsFirst"/>）、普通群保持普通在前，
    /// 再往后是作品分组与角色名（统一口径见 <see cref="CharacterSorting"/>）。
    /// 收藏变更后重排仍走这里，两处不会各说各话
    /// </summary>
    /// <param name="x">候选</param>
    /// <param name="y">候选</param>
    /// <returns>比较结果</returns>
    private int CompareCandidates(GroupCandidate x, GroupCandidate y) =>
        CharacterSorting.Compare(x.Data, y.Data, KindRank);

    /// <summary>档位排名的键：<see cref="_agentsFirst"/> 为真时智能体(0)在普通(1)前，否则反过来</summary>
    /// <param name="character">角色</param>
    /// <returns>排名键，小的在前</returns>
    private int KindRank(CharacterData character) => _agentsFirst ? (character.IsAgent ? 0 : 1) : (character.IsAgent ? 1 : 0);

    /// <summary>候选行的星标被点：切收藏并立刻重排（收藏的顶到最前），不必等下次开窗</summary>
    /// <param name="candidate">点的那一行</param>
    private void OnCandidateFavoriteToggled(GroupCandidate candidate)
    {
        CharacterFavorites.Toggle(candidate.Data.CharacterId);
        _allCandidates.Sort(CompareCandidates);
        ApplyFilter();
    }

    /// <summary>已选变了（勾上或取消）</summary>
    public event Action? PickedChanged;

    /// <summary>成员模型的选项：第一项是「跟随全局」，其余来自全局模型列表</summary>
    public ObservableCollection<SessionModelOption> ModelOptions { get; } = [];

    /// <summary>已选成员（顺序即发言顺序）：候选区勾上就进来，每行带着自己的模型下拉</summary>
    public ObservableCollection<GroupCandidate> PickedMembers { get; } = [];

    /// <summary>当前筛出来的候选（档位 + 搜索词），勾选仍走全量</summary>
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

    /// <summary>
    /// 勾上一位（预选用）
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <param name="modelName">预选的模型名；null 或不在清单里就跟随全局</param>
    public void Pick(string characterId, string? modelName = null)
    {
        if (_allCandidates.FirstOrDefault(x => x.Data.CharacterId == characterId) is not { } candidate) return;
        if (modelName != null && ModelOptions.FirstOrDefault(x => x.ModelName == modelName) is { } model)
            candidate.SelectedModelOption = model;
        candidate.IsPicked = true;
    }

    private void OnCandidateToggled(GroupCandidate candidate, bool picked)
    {
        _picked.Remove(candidate.Data);
        if (picked) _picked.Add(candidate.Data);
        if (picked && !PickedMembers.Contains(candidate)) PickedMembers.Add(candidate);
        else if (!picked) PickedMembers.Remove(candidate);
        PickedChanged?.Invoke();
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

/// <summary>挑群成员时的一个候选角色</summary>
public partial class GroupCandidate : ObservableObject
{
    private readonly Action<GroupCandidate, bool> _toggled;
    private readonly Action<GroupCandidate> _favoriteToggled;

    [ObservableProperty] private bool _isPicked;

    [ObservableProperty] private SessionModelOption? _selectedModelOption;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="data">角色</param>
    /// <param name="toggled">勾选变化时回报（要按勾选顺序排发言）</param>
    /// <param name="favoriteToggled">星标被点时回报（切收藏 + 重排候选）</param>
    /// <param name="modelOptions">成员模型的选项（全窗口共享同一份）</param>
    public GroupCandidate(CharacterData data, Action<GroupCandidate, bool> toggled,
        Action<GroupCandidate> favoriteToggled, IReadOnlyList<SessionModelOption> modelOptions)
    {
        Data = data;
        _toggled = toggled;
        _favoriteToggled = favoriteToggled;
        ModelOptions = modelOptions;
    }

    /// <summary>角色</summary>
    public CharacterData Data { get; }

    /// <summary>曾在这个群里、已退出：勾上会恢复他原来的会话</summary>
    public bool WasMember { get; init; }

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

    /// <summary>是否已收藏（收藏置顶 <see cref="CharacterFavorites"/>)</summary>
    public bool IsFavorite => CharacterFavorites.IsFavorite(Data.CharacterId);

    /// <summary>星标颜色：已收藏金色，未收藏灰</summary>
    public Color FavoriteColor => IsFavorite ? Colors.Gold : Colors.Gray;

    /// <summary>切换收藏（候选行星标）</summary>
    [RelayCommand]
    private void ToggleFavorite() => _favoriteToggled(this);

    /// <summary>类别徽章底色</summary>
    public IBrush KindColor => CharacterKindPresentation.BrushOf(Data);

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(Data);

    partial void OnIsPickedChanged(bool value) => _toggled(this, value);
}
