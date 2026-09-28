using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色工作台左栏那份<b>导航列表</b>：一行一个角色，选中谁右边就编辑谁。
///
/// 这里曾并列着两套模板（照片墙 + 列表）。左栏收窄成导航条之后画廊摆不下了，
/// 照片墙连同它的配置项一并退役——大头像在右主区顶栏与表单里都看得到。
/// </summary>
public partial class CharacterListViewData : ObservableObject
{
    public ObservableCollection<CharacterInfoViewData> Characters { get; } = new();

    /// <summary>
    /// 档位筛选（普通角色 / 智能体）。落进设置，重启后仍是这一档。
    /// </summary>
    public ECharacterKindFilter KindFilter
    {
        get => ConfigManager.Instance.Setting.CharacterKindFilter;
        set
        {
            if (ConfigManager.Instance.Setting.CharacterKindFilter == value) return;
            ConfigManager.Instance.Setting.CharacterKindFilter = value;
            LoadCharacters();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 来源筛选（内置 / 我建的）。与 <see cref="KindFilter"/> 正交——两条轴各筛各的，
    /// 所以「我的智能体」这种组合仍然选得出来。
    /// </summary>
    public ECharacterOriginFilter OriginFilter
    {
        get => ConfigManager.Instance.Setting.CharacterOriginFilter;
        set
        {
            if (ConfigManager.Instance.Setting.CharacterOriginFilter == value) return;
            ConfigManager.Instance.Setting.CharacterOriginFilter = value;
            LoadCharacters();
            OnPropertyChanged();
        }
    }

    /// <summary>搜索关键字：按名字与描述过滤，空则不过滤</summary>
    [ObservableProperty] private string _searchKeyword = string.Empty;

    [ObservableProperty] private bool _isDisplayAllCharacters;

    /// <summary>
    /// 当前选中项。<b>可以为 null</b>：列表空着是一种，从集合里摘掉正被选中的那一项时
    /// ListBox 也会往这里回写 null（撤销新建的占位项就会走到）。
    /// </summary>
    [ObservableProperty] private CharacterInfoViewData? _selectedCharacter;

    private readonly List<CharacterInfoViewData> _characterChacheList = new(20);

    /// <summary>还没入库的那个新角色（顶在列表最前，取消建立即消失）</summary>
    private CharacterInfoViewData? _pending;

    private bool _isInit;

    /// <summary>
    /// 当前选择角色变化事件
    /// </summary>
    public event Action<CharacterInfoViewData?>? EventOnSelectedCharacterChanged;

    /// <summary>
    /// 「新建」的落点，由工作台填。命令留在本类只是因为按钮长在左栏头部；
    /// 真正要做的事（脏检查、开草稿、顶一条占位项）全是工作台那边的账。
    /// </summary>
    public Func<Task>? NewCharacterRequested { get; set; }

    public CharacterListViewData()
    {
        LoadCharacters();
        CharacterManager.Instance.OnCharacterAdded += OnCharacterAdded;
        CharacterManager.Instance.OnCharacterRemoved += OnCharacterRemoved;
        CharacterManager.Instance.OnCharacterUpdated += OnCharacterUpdated;
        CharacterVisibility.ShowShieldedChanged += LoadCharacters;
    }

    private void LoadCharacters()
    {
        _isInit = false;
        Characters.Clear();
        _characterChacheList.Clear();
        foreach (var characterData in CharacterManager.Instance.CharacterDataDictionary)
        {
            CharacterInfoViewData item = new(characterData.Value);
            if (!Matches(item)) continue;
            _characterChacheList.Add(item);
        }

        // 同类内按存档时间倒序,普通角色在前、智能体在后
        _characterChacheList.Sort((x, y) =>
        {
            bool xa = x.IsAgent, ya = y.IsAgent;
            if (xa != ya) return xa ? 1 : -1;
            // 内置卡没有存档时间(全是 0),不补这一刀它们的先后就取决于资源清单的枚举顺序,
            // 换个构建顺序可能就变了。按 CharacterId 排 = 按罗马字名字排,稳定且读得懂。
            int byTime = y.FileDateTime.CompareTo(x.FileDateTime);
            return byTime != 0 ? byTime : string.CompareOrdinal(x.CharacterId, y.CharacterId);
        });

        // 还没入库的新角色不在字典里,重建列表时得自己顶回最前,否则建到一半会被筛没
        if (_pending != null) _characterChacheList.Insert(0, _pending);

        foreach (var x in _characterChacheList)
        {
            Characters.Add(x);
        }

        RefreshSelectedCharacter();
        _isInit = true;
    }

    private void RefreshSelectedCharacter()
    {
        if (SelectedCharacter != null && _characterChacheList.Contains(SelectedCharacter)) return;
        SelectedCharacter = _characterChacheList.Count > 0 ? _characterChacheList[0] : null;
    }

    /// <summary>
    /// 这一项此刻该不该出现在列表里：档位 + 来源 + 内部角色开关 + 屏蔽闸门 + 搜索关键字。
    /// 建列表与「改完之后还算不算数」共用同一份判据，两边不会各说各话。
    /// </summary>
    /// <param name="item">列表项</param>
    /// <returns>该显示返回 True</returns>
    private bool Matches(CharacterInfoViewData item)
    {
        if (KindFilter == ECharacterKindFilter.Chat && !item.Data.IsChat()) return false;
        if (KindFilter == ECharacterKindFilter.Agent && !item.IsAgent) return false;
        if (!MatchesOrigin(item.IsBuiltIn)) return false;
        if (item.Data.IsInternal && !IsDisplayAllCharacters) return false;
        if (!CharacterVisibility.PassesShield(item.Data)) return false;

        return item.Data.MatchesSearch(SearchKeyword);
    }

    /// <summary>
    /// 来源轴的判据：内置卡与用户自己建的卡分开列，免得两类混在一条列表里分不出来。
    /// 与档位轴正交，内部角色与屏蔽角色两道闸门另算，不归这里管。
    /// </summary>
    /// <param name="isBuiltIn">这张卡是否随程序内置</param>
    /// <returns>该显示返回 True</returns>
    private bool MatchesOrigin(bool isBuiltIn) => OriginFilter switch
    {
        ECharacterOriginFilter.BuiltIn => isBuiltIn,
        ECharacterOriginFilter.Mine => !isBuiltIn,
        _ => true,
    };

    partial void OnIsDisplayAllCharactersChanged(bool value)
    {
        LoadCharacters();
    }

    partial void OnSearchKeywordChanged(string value)
    {
        LoadCharacters();
    }

    /// <summary>
    /// 把一个还没入库的新角色顶到列表最前并选中。它只是个占位——
    /// 用户点「创建」之后才真正入库，那时由 <see cref="OnCharacterAdded"/> 认领这一项。
    /// </summary>
    /// <param name="seed">已定好档位的空角色（与草稿改的是同一个实例）</param>
    public void BeginPending(CharacterData seed)
    {
        CancelPending();
        _pending = new CharacterInfoViewData(seed);
        _characterChacheList.Insert(0, _pending);
        Characters.Insert(0, _pending);
        SelectedCharacter = _pending;
    }

    /// <summary>撤掉那个占位项（用户放弃新建）</summary>
    public void CancelPending()
    {
        if (_pending == null) return;

        CharacterInfoViewData stale = _pending;
        _pending = null;
        _characterChacheList.Remove(stale);
        Characters.Remove(stale);
        RefreshSelectedCharacter();
    }

    private void OnCharacterAdded(CharacterData obj)
    {
        // 占位项转正:它包的就是刚入库的这个实例,不必再插一条
        if (_pending != null && ReferenceEquals(_pending.Data, obj))
        {
            _pending.Refresh();
            _pending = null;
            return;
        }

        var characterInfo = new CharacterInfoViewData(obj);
        int index = Math.Max(0, Characters.IndexOf(SelectedCharacter!));
        Characters.Insert(index, characterInfo);
        _characterChacheList.Insert(index, characterInfo);
        RefreshSelectedCharacter();
    }

    private void OnCharacterRemoved(CharacterData obj)
    {
        // 按标识匹配:显示名允许重复,按名字删会误伤同名角色
        Characters.RemvoeItem(x => x.CharacterId == obj.CharacterId);
        _characterChacheList.RemoveAll(x => x.CharacterId == obj.CharacterId);
        RefreshSelectedCharacter();
    }

    /// <summary>
    /// 角色被改写了。实例没换，因此绑定收不到通知，得逐项喊一声重读。
    /// </summary>
    private void OnCharacterUpdated(CharacterData obj)
    {
        foreach (CharacterInfoViewData item in _characterChacheList.ToList())
        {
            if (item.CharacterId != obj.CharacterId) continue;

            item.Refresh();
            // 改完可能就不该在这份列表里了(换了档位、改了名字不再命中搜索)。
            // 留着的话那一行会顶着一个与当前筛选自相矛盾的徽章
            if (Matches(item)) continue;

            _characterChacheList.Remove(item);
            Characters.Remove(item);
        }

        RefreshSelectedCharacter();
    }

    partial void OnSelectedCharacterChanged(CharacterInfoViewData? value)
    {
        if (_isInit) EventOnSelectedCharacterChanged?.Invoke(value);
    }

    [RelayCommand]
    private async Task NewCharacter()
    {
        // 新建不带档位参数(ADR 0043):建出来就是普通角色，要干活在编辑页打开「智能体」
        if (NewCharacterRequested == null) return;
        await NewCharacterRequested();
    }

    [RelayCommand]
    private async Task ImportCharacter()
    {
        var window = new ImportCharacterWindow();
        await window.ShowDialog(UIManager.GetFocusWindow());
    }

    ~CharacterListViewData()
    {
        CharacterManager.Instance.OnCharacterAdded -= OnCharacterAdded;
        CharacterManager.Instance.OnCharacterRemoved -= OnCharacterRemoved;
        CharacterManager.Instance.OnCharacterUpdated -= OnCharacterUpdated;
        CharacterVisibility.ShowShieldedChanged -= LoadCharacters;
    }
}
