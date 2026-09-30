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
/// 挑群成员：候选（筛选 + 搜索）、已选（<b>勾选顺序即发言顺序</b>）与各自的模型。建群与之后加人共用
/// </summary>
public partial class GroupCandidatePicker : ObservableObject
{
    private readonly List<CharacterData> _picked = []; //勾选顺序即发言顺序
    private readonly List<GroupCandidate> _allCandidates;

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
        IReadOnlySet<string>? excludedIds = null, IReadOnlySet<string>? formerIds = null)
    {
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
            .Where(x => !x.IsInternal && GroupChatSessions.CanJoin(x)
                        && excludedIds?.Contains(x.CharacterId) != true
                        && (CharacterVisibility.PassesShield(x) || alwaysListed?.Any(p => p.CharacterId == x.CharacterId) == true))
            .OrderBy(x => x.IsAgent)
            .ThenBy(x => x.CharacterName, StringComparer.CurrentCulture)
            .Select(x => new GroupCandidate(x, OnCandidateToggled, ModelOptions)
            {
                SelectedModelOption = followGlobal,
                WasMember = formerIds?.Contains(x.CharacterId) == true,
            })
            .ToList();
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

    [ObservableProperty] private bool _isPicked;

    [ObservableProperty] private SessionModelOption? _selectedModelOption;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="data">角色</param>
    /// <param name="toggled">勾选变化时回报（要按勾选顺序排发言）</param>
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

    /// <summary>类别徽章底色</summary>
    public IBrush KindColor => CharacterKindPresentation.BrushOf(Data);

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(Data);

    partial void OnIsPickedChanged(bool value) => _toggled(this, value);
}
