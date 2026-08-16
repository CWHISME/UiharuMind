using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;

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
    /// 筛选档位：全部 + 三个可建档位。第 0 档为「全部」，其余按
    /// <see cref="CharacterKindPresentation.CreatableKinds"/> 顺序对齐。
    /// </summary>
    public string[] FilterTags =
    [
        Lang.All,
        ..CharacterKindPresentation.CreatableKinds.Select(CharacterKindPresentation.NameOf),
    ];

    public string FilterTag
    {
        get => FilterTagIndex < 0 || FilterTagIndex >= FilterTags.Length ? FilterTags[0] : FilterTags[FilterTagIndex];
        set => FilterTagIndex = Array.IndexOf(FilterTags, value);
    }

    public int FilterTagIndex
    {
        get => ConfigManager.Instance.Setting.CharacterFilterIndex;
        set
        {
            ConfigManager.Instance.Setting.CharacterFilterIndex = value;
            LoadCharacters();
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilterTag));
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
    public Func<ECharacterKind, Task>? NewCharacterRequested { get; set; }

    public CharacterListViewData()
    {
        LoadCharacters();
        CharacterManager.Instance.OnCharacterAdded += OnCharacterAdded;
        CharacterManager.Instance.OnCharacterRemoved += OnCharacterRemoved;
        CharacterManager.Instance.OnCharacterUpdated += OnCharacterUpdated;
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

        // 同档内按存档时间倒序,档间按枚举顺序(扮演 → 工具人 → 智能体)
        _characterChacheList.Sort((x, y) =>
        {
            if (x.Kind != y.Kind) return x.Kind.CompareTo(y.Kind);
            return y.FileDateTime.CompareTo(x.FileDateTime);
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
    /// 这一项此刻该不该出现在列表里：档位筛选 + 内部角色开关 + 搜索关键字。
    /// 建列表与「改完之后还算不算数」共用同一份判据，两边不会各说各话。
    /// </summary>
    /// <param name="item">列表项</param>
    /// <returns>该显示返回 True</returns>
    private bool Matches(CharacterInfoViewData item)
    {
        // 筛选直读档位:一个角色只有一个身份,不再从挂载列表派生
        if (FilterTagIndex > 0 && item.Kind != CharacterKindPresentation.CreatableKinds[FilterTagIndex - 1])
            return false;
        if (item.Data.IsInternal && !IsDisplayAllCharacters) return false;

        return string.IsNullOrWhiteSpace(SearchKeyword) ||
               item.SearchText.Contains(SearchKeyword.Trim(), StringComparison.OrdinalIgnoreCase);
    }

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
        int index = Math.Max(0, Characters.IndexOf(SelectedCharacter));
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
    private async Task NewCharacter(ECharacterKind? kind)
    {
        if (NewCharacterRequested == null) return;
        await NewCharacterRequested(kind ?? ECharacterKind.Roleplay);
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
    }
}
