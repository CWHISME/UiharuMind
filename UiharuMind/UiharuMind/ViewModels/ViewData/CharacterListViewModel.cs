using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.ViewData;

public partial class CharacterListViewModel : ViewModelBase
{
    public ObservableCollection<CharacterInfoViewData> Characters { get; set; } = new();

    [ObservableProperty] private CharacterInfoViewData _selectedCharacter;

    /// <summary>
    /// 当前选择角色变化事件
    /// </summary>
    public event Action<CharacterInfoViewData> EventOnSelectedCharacterChanged;

    public CharacterListViewModel()
    {
        LoadCharacters();
        _selectedCharacter = Characters[0];
    }

    private void LoadCharacters()
    {
        foreach (var characterData in CharacterManager.Instance.CharacterDataDictionary)
        {
            Characters.Add(new CharacterInfoViewData(characterData.Value));
        }

        for (int i = 0; i < 20; i++)
        {
            Characters.Add(new CharacterInfoViewData(CharacterManager.Instance.GetCharacterData("")));
        }
    }

    partial void OnSelectedCharacterChanged(CharacterInfoViewData value)
    {
        EventOnSelectedCharacterChanged.Invoke(value);
    }

    [RelayCommand]
    public void AddCharacter()
    {
        Log.Debug("Add Character");
    }
}