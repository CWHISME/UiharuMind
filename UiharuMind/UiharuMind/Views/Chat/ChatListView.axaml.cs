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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.OtherViews;

public partial class ChatListView : UserControl
{
    public ChatListView()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<ChatListViewModel>();
    }
}