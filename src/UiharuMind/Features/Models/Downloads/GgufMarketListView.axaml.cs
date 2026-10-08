using Avalonia.Controls;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// GGUF 仓库的左栏：搜索、排序与结果列表（滚到底续下一页）
/// </summary>
public partial class GgufMarketListView : UserControl
{
    private const double NextPageThreshold = 200; //离底多少像素就续取下一页

    public GgufMarketListView()
    {
        InitializeComponent();
        ResultList.AddHandler(ScrollViewer.ScrollChangedEvent, OnResultsScrollChanged);
    }

    // 快滚到底时续取下一页；第一页撑不满列表时，结果变多引起的范围变化也会走到这里，一路续到撑满为止
    private void OnResultsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer viewer) return;
        if (viewer.Offset.Y < viewer.Extent.Height - viewer.Viewport.Height - NextPageThreshold) return;
        (DataContext as GgufMarketData)?.LoadNextPage();
    }
}
