using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Resources.Lang;
using UiharuMind.Views.Windows.Characters;
using UiharuMind.Utils;
using UiharuMind.Views;

namespace UiharuMind.ViewModels.ViewData;

public partial class CharacterListViewData : ObservableObject
{
    public ObservableCollection<CharacterInfoViewData> Characters { get; set; } = new();

    /// <summary>
    /// 筛选
    /// </summary>
    public string[] FilterTags = new string[] { Lang.All, Lang.Tool, Lang.Character };

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
            // 筛选
            if (FilterTag == Lang.All || (FilterTag == Lang.Tool && characterData.Value.IsTool) ||
                (FilterTag == Lang.Character && !characterData.Value.IsTool))
            {
                if (characterData.Value.IsHideDefault && !IsDisplayAllCharacters) continue;
                var characterInfo = new CharacterInfoViewData(characterData.Value);
                // if (characterData.Value.IsTool) _characterChacheList.Add(characterInfo);
                // // else Characters.Insert(0, characterInfo);
                // else 
                _characterChacheList.Add(characterInfo);
            }
        }

        _characterChacheList.Sort((x, y) =>
        {
            if (x.IsRole && !y.IsRole) return -1;
            if (!x.IsRole && y.IsRole) return 1;
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
        Characters.RemvoeItem(x => x.Name == obj.CharacterName);
        _characterChacheList.RemoveAll(x => x.Name == obj.CharacterName);
        RefreshSelectedCharacter();
    }

    partial void OnSelectedCharacterChanged(CharacterInfoViewData value)
    {
        if (_isInit) EventOnSelectedCharacterChanged?.Invoke(value);
    }

    [RelayCommand]
    private void AddCharacter()
    {
        UIManager.ShowEditCharacterWindow(null, (data) => { data.TryAddToNewCharacterData(); });
    }

    [RelayCommand]
    private async Task ImportCharacter()
    {
        // UIManager.ShowWindow<ImportCharacterWindow>();
        var window = new ImportCharacterWindow();
        await window.ShowDialog(UIManager.GetFoucusWindow());
    }

    ~CharacterListViewData()
    {
        CharacterManager.Instance.OnCharacterAdded -= OnCharacterAdded;
        CharacterManager.Instance.OnCharacterRemoved -= OnCharacterRemoved;
    }
}