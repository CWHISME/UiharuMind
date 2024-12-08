using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Windows.Characters;

namespace UiharuMind.ViewModels.ViewData;

public partial class CharacterListViewData : ObservableObject
{
    public ObservableCollection<CharacterInfoViewData> Characters { get; set; } = new();

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
        _selectedCharacter = Characters[0];
    }

    private void LoadCharacters()
    {
        foreach (var characterData in CharacterManager.Instance.CharacterDataDictionary)
        {
            var characterInfo = new CharacterInfoViewData(characterData.Value);
            if (characterData.Value.IsTool) Characters.Add(characterInfo);
            else Characters.Insert(0, characterInfo);
        }
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
}