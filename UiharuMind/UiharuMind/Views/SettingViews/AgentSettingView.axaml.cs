/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using UiharuMind.ViewModels.ViewData.SettingViewData;

namespace UiharuMind.Views.SettingViews;

public partial class AgentSettingView : UserControl
{
    public AgentSettingView()
    {
        InitializeComponent();
        DataContext = new AgentSettingViewData();
    }
}
