using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Models.Downloads;

public partial class ModelDownloadView : UserControl
{
    private const double NarrowWidth = 640;
    private bool? _isNarrow;
    private ModelDownloadPageData? _data;

    public ModelDownloadView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 窄窗：详情做成盖在列表上的抽屉
    /// </summary>
    public bool IsNarrow => _isNarrow == true;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool isNarrow = e.NewSize.Width < NarrowWidth;
        if (_isNarrow == isNarrow) return;
        _isNarrow = isNarrow;
        Classes.Set("narrow", isNarrow);
        BodyGrid.ColumnDefinitions[1].Width = isNarrow ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
        Grid.SetColumn(DetailPane, isNarrow ? 0 : 1);
        UpdateDrawer();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_data != null) _data.PropertyChanged -= OnDataPropertyChanged;
        _data = DataContext as ModelDownloadPageData;
        if (_data != null) _data.PropertyChanged += OnDataPropertyChanged;
        UpdateDrawer();
    }

    private void OnDataPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModelDownloadPageData.Detail)) UpdateDrawer();
    }

    // 宽窗详情常驻；窄窗只在打开仓库时盖上来
    private void UpdateDrawer()
    {
        bool hasDetail = _data?.Detail != null;
        DetailPane.IsVisible = !IsNarrow || hasDetail;
        ListPane.IsVisible = !IsNarrow || !hasDetail;
    }
}
