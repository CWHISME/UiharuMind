using System;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.AutoClick;

public partial class PlaybackIndicatorWindow : UiharuWindowBase
{
    private int _currentRound = 0;
    private int _totalRounds = 1;

    public PlaybackIndicatorWindow()
    {
        InitializeComponent();

        this.SetSimpledecorationWindow(isTopmost: true);
        Width = 220;
        Height = 90;
        CanResize = false;
        ShowInTaskbar = false;
    }

    public override bool IsCacheWindow => false;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // 将指示器窗口设置到鼠标位置附近
        this.SetWindowToMousePosition(
            Avalonia.Layout.HorizontalAlignment.Right,
            Avalonia.Layout.VerticalAlignment.Top,
            offsetX: 20,
            offsetY: 20
        );
    }

    public void UpdateProgress(int current, int total)
    {
        _currentRound = current;
        _totalRounds = total;
        
        Dispatcher.UIThread.Post(() =>
        {
            RoundText.Text = total > 1 ? $"第 {current}/{total} 轮" : "执行中...";
        });
    }

    public void FlashIndicator()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var border = this.FindControl<Border>("IndicatorBorder");
            if (border != null)
            {
                border.Background = Avalonia.Media.Brushes.LightBlue;
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(200);
                    border.Background = Avalonia.Media.Brushes.LightBlue;
                }, DispatcherPriority.Background);
            }
        });
    }
}
