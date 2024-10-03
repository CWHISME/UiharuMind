using Avalonia.Controls;

namespace UiharuMind.ViewModels.UIHolder;

/// <summary>
/// 自动滚动的 ScrollViewer 容器至底部(用户操作后取消)
/// </summary>
public class ScrollViewerAutoScrollHolder
{
    private bool _isAutoScrolling = true;

    public ScrollViewerAutoScrollHolder(ScrollViewer scrollViewer)
    {
        scrollViewer.ScrollToEnd();
        scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = e.Source as ScrollViewer;
        if (scrollViewer == null) return;
        bool isManualScroll = e.ExtentDelta.Y == 0;
        if (isManualScroll && e.OffsetDelta.Y > 0)
        {
            // 用户向下滚动
            _isAutoScrolling = false;
        }
        else if (isManualScroll && e.OffsetDelta.Y < 0)
        {
            // 用户向上滚动
            _isAutoScrolling = false;
        }
        else if (scrollViewer.ScrollBarMaximum.Y > 0 &&
                 scrollViewer.Offset.Y >= scrollViewer.ScrollBarMaximum.Y - e.ExtentDelta.Y)
        {
            // 有进度条，且用户手动或自动滚动到了底部，继续自动滚动
            _isAutoScrolling = true;
        }
        //Log.Debug($"scrollViewer.ScrollBarMaximum.Y: {scrollViewer.ScrollBarMaximum.Y}, scrollViewer.Offset.Y: {scrollViewer.Offset.Y}, e.ExtentDelta.Y: {e.ExtentDelta.Y}");

        // 如果需要自动滚动到底部
        if (_isAutoScrolling)
        {
            scrollViewer.ScrollToEnd();
        }

        e.Handled = true;
    }
}