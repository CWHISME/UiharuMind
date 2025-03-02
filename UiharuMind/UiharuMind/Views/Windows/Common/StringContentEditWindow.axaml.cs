using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.Common;

public partial class StringContentEditWindow : Window
{
    // public static void Show(string content, Action<string> callback)
    // {
    //     UIManager.ShowWindow<StringContentEditWindow>(x =>
    //     {
    //         x.DataContext = new StringContentEditWindowViewModel(content, callback);
    //     });
    // }

    public StringContentEditWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var model = (DataContext as StringContentEditWindowViewModel);
        model?.TriggerCallback();
        Close(model?.ContentStr);
    }
}

public partial class StringContentEditWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _contentStr;

    private Action<string>? _callback;

    public StringContentEditWindowViewModel(string contentStr, Action<string>? callback)
    {
        _contentStr = contentStr;
        _callback = callback;
    }

    public void TriggerCallback()
    {
        _callback?.Invoke(ContentStr);
    }
}