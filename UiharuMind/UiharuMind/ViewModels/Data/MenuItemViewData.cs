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

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UiharuMind.ViewModels;

public class MenuItemViewData : ObservableObject
{
    public string? MenuHeader { get; set; }
    public string? MenuIconName { get; set; }
    public MenuPages Key { get; set; }
    public string? Status { get; set; }

    public bool IsSeparator { get; set; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ObservableCollection<MenuItemViewData> Children { get; set; } = new();

    public ICommand ActivateCommand { get; set; }

    public MenuItemViewData()
    {
        ActivateCommand = new RelayCommand(OnActivate);
    }

    private void OnActivate()
    {
        if (IsSeparator) return;
        // Messenger.Send(Key);
        // WeakReferenceMessenger.Default.Send(Key);
        App.JumpToPage(Key);
    }
}