using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class ChatInfoView : UserControl
{
    private ChatInfoModel _model;

    public ChatInfoView()
    {
        InitializeComponent();

        _model = App.ViewModel.GetViewModel<ChatInfoModel>();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        OnChatSessionChanged();
        _model.OnEventChatSessionChanged += OnChatSessionChanged;
    }

    private void OnChatSessionChanged()
    {
        ChatPluginsPanel.Children.Clear();
        foreach (var plugin in _model.ChatPluginList)
        {
            ChatPluginsPanel.Children.Add(plugin.View);
        }

        // Log.Debug("OnChatSessionChanged");
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _model.OnEventChatSessionChanged -= OnChatSessionChanged;
    }
}