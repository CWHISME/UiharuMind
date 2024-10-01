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
    }

    private void OnLogChange(LogItem obj)
    {
        Dispatcher.UIThread.Post(() => { Items.Add(obj); }, DispatcherPriority.Background);
    }
}