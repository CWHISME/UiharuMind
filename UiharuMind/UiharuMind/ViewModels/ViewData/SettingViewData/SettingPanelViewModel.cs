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

using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.ViewModels.SettingViewData;

public class SettingPanelViewModel : ObservableObject
{
    
    public string Title { get; set; }
    
    public string Description { get; set; }
    
}