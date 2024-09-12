using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.Views.Pages;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
    }

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        (((ChatPageData)DataContext!)).PaneWidth += (float)e.Vector.X;
    }
    
    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        (((ChatPageData)DataContext!)).RightPaneWidth -= (float)e.Vector.X;
    }
}