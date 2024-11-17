/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class HomePageData : PageDataBase
{
    protected override Control CreateView => new HomePage();

    // [ObservableProperty] private CharacterInfoViewData _characterInfo;
    [ObservableProperty] private float _leftPaneWidth = 200;

    [ObservableProperty] private CharacterListViewData _characterListViewData;

    public HomePageData()
    {
        _characterListViewData = new CharacterListViewData();
        // CharacterInfo = _characterListViewData.SelectedCharacter;
    }

    // public override void OnEnable()
    // {
    //     base.OnEnable();
    //     CharacterInfo = _characterListViewData.SelectedCharacter;
    //     _characterListViewData.EventOnSelectedCharacterChanged += OnSelectedCharacterChanged;
    // }
    //
    // private void OnSelectedCharacterChanged(CharacterInfoViewData obj)
    // {
    //     CharacterInfo = obj;
    // }
    //
    // public override void OnDisable()
    // {
    //     base.OnDisable();
    //     _characterListViewData.EventOnSelectedCharacterChanged -= OnSelectedCharacterChanged;
    // }
}