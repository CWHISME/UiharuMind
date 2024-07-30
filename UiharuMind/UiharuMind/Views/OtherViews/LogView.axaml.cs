using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Views.OtherViews;

public partial class LogView : UserControl
{
    public ObservableCollection<LogItem> Items { get; } = new();

    //记录是否在底部
    private bool _isAtBottom = true;

    public LogView()
    {
        InitializeComponent();
        DataContext = this;

        foreach (var item in LogManager.Instance.LogItems)
        {
            OnLogChange(item);
        }

        LogManager.Instance.OnLogChange += OnLogChange;
        Items.CollectionChanged += OnLogsCollectionChanged;
        Viewer.ScrollChanged += OnScrollChanged;
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _isAtBottom)
        {
            Dispatcher.UIThread.Post(() => { Viewer.ScrollToEnd(); }, DispatcherPriority.Background);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _isAtBottom = Viewer.Offset.Y + Viewer.Viewport.Height >= Viewer.Extent.Height;
    }

    private void OnLogChange(LogItem obj)
    {
        Items.Add(obj);
    }
}