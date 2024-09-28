using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.UIHolder;

namespace UiharuMind.Views.OtherViews;

public partial class LogView : UserControl
{
    public ObservableCollection<LogItem> Items { get; } = new();

    private ScrollViewerAutoScrollHolder _scrollHolder;
    // //记录是否在底部
    // private bool _isAtBottom = true;

    public LogView()
    {
        InitializeComponent();
        DataContext = this;


        foreach (var item in LogManager.Instance.LogItems)
        {
            OnLogChange(item);
        }

        LogManager.Instance.OnLogChange += OnLogChange;
        _scrollHolder = new ScrollViewerAutoScrollHolder(Viewer);
        // Items.CollectionChanged += OnLogsCollectionChanged;
        // Viewer.ScrollChanged += OnScrollChanged;
        // for (int i = 0; i < 100; i++)
        // {
        //     Log.Debug("测试日志" + i);
        // }
    }

    // private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    // {
    //     if (e.Action == NotifyCollectionChangedAction.Add && _isAtBottom)
    //     {
    //         Dispatcher.UIThread.InvokeAsync(async () =>
    //         {
    //             // 等待UI更新
    //             await Task.Delay(100);
    //             Viewer.ScrollToEnd();
    //             _isAtBottom = true;
    //         }, DispatcherPriority.Background);
    //     }
    // }

    // private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    // {
    //     _isAtBottom = Viewer.Offset.Y >= Viewer.ScrollBarMaximum.Y - Viewer.Viewport.Height;
    //     //+ Viewer.Viewport.Height + 20 >= Viewer.Extent.Height;
    // }

    private void OnLogChange(LogItem obj)
    {
        Items.Add(obj);
    }
}