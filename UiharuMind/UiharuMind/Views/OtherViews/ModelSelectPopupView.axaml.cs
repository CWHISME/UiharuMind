using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace UiharuMind.Views.OtherViews;

public partial class ModelSelectPopupView : UserControl
{
    public ModelSelectPopupView()
    {
        InitializeComponent();
    }

    public void ShowPopup(Control? control)
    {
        if(control==null) return;
        SelectionPopup.PlacementTarget = control;
        SelectionPopup.IsOpen = true;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        // 检查焦点是否真的离开了 Popup
        // if (!SelectionPopup.IsKeyboardFocusWithin)
        {
            SelectionPopup.IsOpen = false;
        }
    }
}