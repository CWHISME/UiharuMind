using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Motion;

namespace UiharuMind.Features.Models.Usage;

public partial class UsagePage : UserControl
{
    private UsagePageData? _viewModel;

    public UsagePage()
    {
        InitializeComponent();
        PropertyChanged += OnPagePropertyChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null) _viewModel.ChartsRebuilt -= OnChartsRebuilt;
        _viewModel = DataContext as UsagePageData;
        if (_viewModel != null) _viewModel.ChartsRebuilt += OnChartsRebuilt;
    }

    private void OnPagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && IsVisible) RepaintCharts();
    }

    private void OnChartsRebuilt()
    {
        //页面不可见时画布不会出帧，补了也白补——切回来时 IsVisible 那条路会补
        if (IsVisible) RepaintCharts();
    }

    /// <summary>
    /// 逼两张图各重画一次。
    ///
    /// <para>
    /// 卡点在 LiveCharts 的测量闸门：<c>Chart.Measure</c> 只在「画布出过比上次测量更新的帧」
    /// 时才放行。两种情况下这条不成立，重建就被吞掉，图表一直空白到下次滚动为止：
    /// 切页回来（折叠期间画布一帧都没出）、图表静止时换数据（静止的图表不会再出帧）。
    /// </para>
    /// <para>
    /// 所以数据每换过一轮都补上缺的两步：先标脏画布逼它出帧，等它真出帧了再请求一次测量。
    /// </para>
    /// </summary>
    private void RepaintCharts()
    {
        foreach (IChartView chart in this.GetVisualDescendants().OfType<IChartView>())
        {
            RepaintChart(chart);
        }
    }

    private static void RepaintChart(IChartView chart)
    {
        CoreMotionCanvas canvas = chart.CoreCanvas;
        canvas.Validated += OnCanvasValidated;
        canvas.Invalidate();

        void OnCanvasValidated(CoreMotionCanvas sender)
        {
            sender.Validated -= OnCanvasValidated;
            //出帧发生在渲染线程，测量只能回 UI 线程做；不带节流，出帧后立刻测
            Dispatcher.UIThread.Post(
                () => chart.CoreChart.Update(new ChartUpdateParams
                {
                    IsAutomaticUpdate = false,
                    Throttling = false,
                }),
                DispatcherPriority.Normal);
        }
    }
}
