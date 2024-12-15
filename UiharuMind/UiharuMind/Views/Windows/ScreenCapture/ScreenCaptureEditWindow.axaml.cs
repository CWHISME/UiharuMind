using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using UiharuMind.Utils;

namespace UiharuMind.Views.Windows.ScreenCapture;

public partial class ScreenCaptureEditWindow : Window
{
    private Bitmap _source;
    private Action<Bitmap> _onClose;

    public int BtnHeight { get; set; } = 45;

    public ScreenCaptureEditWindow(Bitmap source, PixelPoint position, Size size, Action<Bitmap> onClose)
    {
        _source = source;
        _onClose = onClose;
        int offset = (int)(App.ScreensService.Scaling * 5);
        var pos = new PixelPoint(position.X - offset, position.Y - offset);
        Width = size.Width + 10;
        Height = size.Height + BtnHeight + 10;
        MaxHeight = Height;
        MinHeight = Height;
        MaxWidth = Width;
        MinWidth = Width;

        CanResize = false;
        this.SetSimpledecorationPureWindow();
        DataContext = this;
        InitializeComponent();

        Position = pos; //position;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ImageContent.Source = _source;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerUpdateKind pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed && e.ClickCount >= 2)
        {
            SafeClose();
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    // private void SaveButton_Click(object? sender, RoutedEventArgs e)
    // {
    //     SafeClose();
    // }

    private void SafeClose()
    {
        _onClose.Invoke(_source);
        Close();
    }
}