using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiharuMind.Features.Models.Downloads;

public partial class ModelRepoDetailView : UserControl
{
    private ModelRepoDetailData? _data;

    public ModelRepoDetailView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_data != null) _data.PropertyChanged -= OnDataPropertyChanged;
        _data = DataContext as ModelRepoDetailData;
        if (_data != null) _data.PropertyChanged += OnDataPropertyChanged;
    }

    // 量化多的仓库推荐项常在折叠线以下：列完就把它滚进视野
    private void OnDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModelRepoDetailData.IsLoading) || _data is not { IsLoading: false } data) return;
        int index = data.Rows.ToList().FindIndex(x => x.IsRecommended);
        if (index < 0) return;
        Dispatcher.UIThread.Post(() => QuantList.ContainerFromIndex(index)?.BringIntoView(), DispatcherPriority.Background);
    }
}
