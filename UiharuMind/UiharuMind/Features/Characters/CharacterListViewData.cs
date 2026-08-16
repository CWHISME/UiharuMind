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
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Configs;

namespace UiharuMind.Features.Characters;

public partial class CharacterListViewData : ObservableObject
{
    public ObservableCollection<CharacterInfoViewData> Characters { get; set; } = new();

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
        }
    }

    [ObservableProperty] private bool _isDisplayAllCharacters;

    [ObservableProperty] private CharacterInfoViewData _selectedCharacter;
    private List<CharacterInfoViewData> _characterChacheList = new List<CharacterInfoViewData>(20);

    private bool _isInit = false;

    public bool IsPhotoListView
    {
        get => ConfigManager.Instance.Setting.IsCharacterPhotoListView;
        set
        {
            ConfigManager.Instance.Setting.IsCharacterPhotoListView = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 当前选择角色变化事件
    /// </summary>
    public event Action<CharacterInfoViewData>? EventOnSelectedCharacterChanged;

    public CharacterListViewData()
    {
        LoadCharacters();
        CharacterManager.Instance.OnCharacterAdded += OnCharacterAdded;
        CharacterManager.Instance.OnCharacterRemoved += OnCharacterRemoved;
        _selectedCharacter = Characters[0];
    }

    private void LoadCharacters()
    {
        _isInit = false;
        Characters.Clear();
        _characterChacheList.Clear();
        foreach (var characterData in CharacterManager.Instance.CharacterDataDictionary)
        {
            // 筛选直读档位:一个角色只有一个身份,不再从挂载列表派生
            if (FilterTagIndex > 0 &&
                characterData.Value.Kind != CharacterKindPresentation.CreatableKinds[FilterTagIndex - 1]) continue;
            if (characterData.Value.IsInternal && !IsDisplayAllCharacters) continue;

            _characterChacheList.Add(new CharacterInfoViewData(characterData.Value));
        }

        // 同档内按存档时间倒序,档间按枚举顺序(扮演 → 工具人 → 智能体)
        _characterChacheList.Sort((x, y) =>
        {
            if (x.Kind != y.Kind) return x.Kind.CompareTo(y.Kind);
            return y.FileDateTime.CompareTo(x.FileDateTime);
        });
        foreach (var x in _characterChacheList)
        {
            Characters.Add(x);
        }

        RefreshSelectedCharacter();
        _isInit = true;
    }

    private void RefreshSelectedCharacter()
    {
        if (_characterChacheList.IndexOf(SelectedCharacter) < 0)
            SelectedCharacter = _characterChacheList.Count > 0 ? _characterChacheList[0] : new CharacterInfoViewData();
    }

    partial void OnIsDisplayAllCharactersChanged(bool value)
    {
        LoadCharacters();
    }

    private void OnCharacterAdded(CharacterData obj)
    {
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

    partial void OnSelectedCharacterChanged(CharacterInfoViewData value)
    {
        if (_isInit) EventOnSelectedCharacterChanged?.Invoke(value);
    }

    /// <summary>
    /// 新建角色。<b>先定档再进表单</b>——三个档位的表单面孔差得太多，
    /// 进去再翻旗标会让人对着一堆当前档用不上的字段发愣。
    /// </summary>
    /// <param name="kind">档位；为空时按角色扮演建</param>
    [RelayCommand]
    private void AddCharacter(ECharacterKind? kind)
    {
        CharacterData character = new() { Kind = kind ?? ECharacterKind.Roleplay };
        // 智能体预填工作循环那一节:它是弱模型最依赖的几条,而现在它归角色提示词管(ADR 0004),
        // 不预填就等于新建出来的智能体默认少了这段。用户可以照常改写或删掉
        if (character.Kind.IsAgent()) character.Template = AgentToolPrompts.AgentWorkLoop;

        CharacterWindows.ShowEditCharacterWindow(new CharacterInfoViewData(character),
            x => x.TryAddToNewCharacterData());
    }

    [RelayCommand]
    private async Task ImportCharacter()
    {
        // UIManager.ShowWindow<ImportCharacterWindow>();
        var window = new ImportCharacterWindow();
        await window.ShowDialog(UIManager.GetFocusWindow());
    }

    ~CharacterListViewData()
    {
        CharacterManager.Instance.OnCharacterAdded -= OnCharacterAdded;
        CharacterManager.Instance.OnCharacterRemoved -= OnCharacterRemoved;
    }
}