using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using UiharuMind.Core;

namespace UiharuMind.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        App.NotificationManager = new WindowNotificationManager(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        UiharuCoreManager.Instance.Input.Stop();
        base.OnClosing(e);
    }
}