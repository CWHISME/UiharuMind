using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.OtherViews;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<ChatViewModel>();
    }
}