using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.Utils;
using UiharuMind.ViewModels;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class HelpWindow : UiharuWindowBase
{
    // public string HelpText { get; set; }

    public HelpWindow()
    {
        InitializeComponent();

        // HelpText = EmbeddedResourcesUtils.Read("Help.md");
        DataContext = App.ViewModel.GetPage(MenuPages.MenuHelpKey);
    }

    // protected override void OnLoaded(RoutedEventArgs e)
    // {
    //     base.OnLoaded(e);
    //     MarkdownViewer.SimpleSetMarkdownText = HelpText;
    // }
}