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