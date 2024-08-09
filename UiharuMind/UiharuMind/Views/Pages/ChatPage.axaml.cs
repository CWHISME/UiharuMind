using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace UiharuMind.Views.Pages;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
    }

    // private void OpenPopupButton_Click(object? sender, RoutedEventArgs e)
    // {
    //     // 计算弹出窗口的位置
    //     var button = (Button)sender;
    //     var point = button.TranslatePoint(new Point(), this);
    //
    //     // 设置Popup的位置
    //     SelectionPopup.PlacementTarget = button;
    //     SelectionPopup.VerticalOffset = point?.Y ?? 0 + button.Bounds.Height;
    //     SelectionPopup.HorizontalOffset = point?.X ?? 0;
    //
    //     // 显示Popup
    //     SelectionPopup.IsOpen = true;
    // }
    // private void OpenModelSelectView(object? sender, RoutedEventArgs e)
    // {
    //     ModelSelectPopupView.ShowPopup(sender as Control);
    // }
}