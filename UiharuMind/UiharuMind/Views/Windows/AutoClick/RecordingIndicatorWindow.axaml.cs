using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.AutoClick;

public partial class RecordingIndicatorWindow : UiharuWindowBase
{
    private int _actionCount = 0;

    public RecordingIndicatorWindow()
    {
        InitializeComponent();

        this.SetSimpledecorationWindow(isTopmost: true);
        Width = 200;
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

    protected override void OnInitialized()
    {
        base.OnInitialized();
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
    }

    public void UpdateActionCount(int count)
    {
        _actionCount = count;
        Dispatcher.UIThread.Post(() => { ActionCountText.Text = $"已录制: {count} 个动作"; });
    }

    public void FlashIndicator()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var border = this.FindControl<Border>("IndicatorBorder");
            if (border != null)
            {
                border.Background = Avalonia.Media.Brushes.Red;
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(200);
                    border.Background = Avalonia.Media.Brushes.LightPink;
                }, DispatcherPriority.Background);
            }
        });
    }
}