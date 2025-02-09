using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    [ObservableProperty] private CharacterInfoViewData _selectedCharacter;

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
        Characters.Clear();
        foreach (var characterData in CharacterManager.Instance.CharacterDataDictionary)
        {
            // 筛选
            if (FilterTag == Lang.All || (FilterTag == Lang.Tool && characterData.Value.IsTool) ||
                (FilterTag == Lang.Character && !characterData.Value.IsTool))
            {
                var characterInfo = new CharacterInfoViewData(characterData.Value);
                if (characterData.Value.IsTool) Characters.Add(characterInfo);
                else Characters.Insert(0, characterInfo);
            }
        }
    }

    private void OnCharacterAdded(CharacterData obj)
    {
        Characters.Insert(Math.Max(0, Characters.IndexOf(SelectedCharacter)), new CharacterInfoViewData(obj));
    }

    private void OnCharacterRemoved(CharacterData obj)
    {
        Characters.RemvoeItem(x => x.Name == obj.CharacterName);
    }

    partial void OnSelectedCharacterChanged(CharacterInfoViewData value)
    {
        EventOnSelectedCharacterChanged?.Invoke(value);
    }

    [RelayCommand]
    public void AddCharacter()
    {
        CharacterEditWindow.Show(null, (data) => { data.TryAddToNewCharacterData(); });
    }

    [RelayCommand]
    public void ImportCharacter()
    {
        UIManager.ShowWindow<ImportCharacterWindow>();
    }

    ~CharacterListViewData()
    {
        CharacterManager.Instance.OnCharacterAdded -= OnCharacterAdded;
        CharacterManager.Instance.OnCharacterRemoved -= OnCharacterRemoved;
    }
}