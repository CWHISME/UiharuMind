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
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.OtherViews;

public partial class ChatView : UserControl
{
    private ScrollViewerAutoScrollHolder _scrollViewerAutoScrollHolder;

    public ChatView()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<ChatViewModel>();
        _scrollViewerAutoScrollHolder = new ScrollViewerAutoScrollHolder(Viewer);
    }

    // protected override void OnLoaded(RoutedEventArgs e)
    // {
    //     base.OnLoaded(e);
    //     App.ViewModel.GetViewModel<ChatViewModel>().OnEventChatSessionChanged += OnChatSessionChanged;
    // }
    //
    // private void OnChatSessionChanged(ChatSessionViewData chatSessionViewData)
    // {
    //     //TODO:比如翻译则单独以翻译方式处理
    //     Log.Debug("OnChatSessionChanged");
    // }
    //
    // protected override void OnUnloaded(RoutedEventArgs e)
    // {
    //     base.OnUnloaded(e);
    //     App.ViewModel.GetViewModel<ChatViewModel>().OnEventChatSessionChanged -= OnChatSessionChanged;
    // }
}