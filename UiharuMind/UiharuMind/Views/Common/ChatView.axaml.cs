using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
}