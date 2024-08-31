using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels;

namespace UiharuMind.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnTapedMenuIcon(object? sender, TappedEventArgs e)
    {
        var model = ((MainViewModel)DataContext!);
        model.IsMenuVisible = !model.IsMenuVisible;
    }
}