using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using UiharuMind.Core;
using UiharuMind.Services;
using UiharuMind.ViewModels;
using UiharuMind.Views;

namespace UiharuMind;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (sender, e) => { UiharuCoreManager.Instance.Dispose(); };
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            FilesService = new FilesService(desktop.MainWindow);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public new static App? Current => Application.Current as App;

    public FilesService? FilesService { get; private set; }
    public WindowNotificationManager? NotificationManager { get; set; }
}