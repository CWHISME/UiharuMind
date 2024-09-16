using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.ViewModels;
using UiharuMind.Views;
using Ursa.Controls;

namespace UiharuMind;

public partial class App : Application, ILogger
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
            // desktop.MainWindow = new MainWindow
            // {
            //     DataContext = new MainViewModel()
            // };
            DummyWindow = new DummyWindow();

            Clipboard = new ClipboardService(DummyWindow);
            FilesService = new FilesService(DummyWindow);
            ScreensService = new ScreensService(DummyWindow);
            MessageService = new MessageService(DummyWindow);
            ModelService = new ModelService();

            desktop.MainWindow = DummyWindow;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();

        LogManager.Instance.Logger = this;
        Lang.Culture = CultureInfo.CurrentCulture;
        UiharuCoreManager.Instance.Init();
    }

    // public new static App Current => (App)Application.Current!;
    public static DummyWindow DummyWindow { get; private set; }
    public static ClipboardService Clipboard { get; private set; }
    public static FilesService FilesService { get; private set; }
    public static ScreensService ScreensService { get; private set; }
    public static ModelService ModelService { get; private set; }
    public static MainViewModel ViewModel => DummyWindow.MainViewModel;
    public static MessageService MessageService { get; private set; }

    public void Debug(string message)
    {
        Console.WriteLine(message);
    }

    public void Warning(string message)
    {
        Console.WriteLine(message);
    }

    public void Error(string message)
    {
        Console.WriteLine(message);
        MessageService.ShowErrorMessage(message);
    }

    private void OnQuitClick(object? sender, EventArgs e)
    {
    }

    private void OnAboutClick(object? sender, EventArgs e)
    {
    }

    private void OnOpenClick(object? sender, EventArgs e)
    {
        DummyWindow.LaunchMainWindow();
    }
}