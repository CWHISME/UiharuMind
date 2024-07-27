using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Views.OtherViews;

public partial class LogView : UserControl
{
    public ObservableCollection<string> Items { get; } = new();

    public LogView()
    {
        InitializeComponent();
        DataContext = this;

        foreach (var item in LogManager.Instance.LogItems)
        {
            OnLogChange(item);
        }

        LogManager.Instance.OnLogChange += OnLogChange;
    }

    private void OnLogChange(LogItem obj)
    {
        Items.Add(obj.ToString());
    }
}