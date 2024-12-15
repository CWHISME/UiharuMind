using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 当拷贝图片时，显示一个按钮是否将图片固定到屏幕(类似截图后效果)
/// </summary>
public partial class QuickPinImageTipWindow : QuickFloatingWindowBase
{
    public static void Show(Bitmap image)
    {
        UIManager.ShowWindow<QuickPinImageTipWindow>(x => x.SetImage(image));
    }

    public QuickPinImageTipWindow()
    {
        InitializeComponent();
    }

    private Bitmap? _image;

    public void SetImage(Bitmap image)
    {
        _image = image;
    }

    private void OnMainButtonClock(object? sender, RoutedEventArgs e)
    {
        UIManager.ShowPreviewImageWindowAtMousePosition(_image);
    }
}