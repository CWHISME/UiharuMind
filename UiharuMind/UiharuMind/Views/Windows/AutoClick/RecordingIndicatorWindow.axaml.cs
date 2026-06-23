using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UiharuMind.Core.Configs;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.AutoClick;

public partial class RecordingIndicatorWindow : UiharuWindowBase
{
    private DateTime _lastFlashTime = DateTime.MinValue;
    private bool _isCountUpdateQueued;
    private bool _isFlashResetQueued;
    private int _pendingActionCount;

    public RecordingIndicatorWindow()
    {
        InitializeComponent();

        this.SetSimpledecorationPureWindow(isTopmost: true);
        this.SetNonInteractiveOverlayWindow();
        Width = 200;
        Height = 82;
        CanResize = false;
        ShowInTaskbar = false;
        StopShortcutText.Text = string.Format(LocalizationManager.Instance.GetString("AutoClickStopShortcutTips"),
            ConfigManager.Instance.Setting.QuickAutoClickShortcut);
    }

    public override bool IsCacheWindow => false;
    public override bool ContributesToMacRegularMode => false;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.SetNonInteractiveOverlayWindow();
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
        _pendingActionCount = count;
        if (_isCountUpdateQueued) return;
        _isCountUpdateQueued = true;

        Dispatcher.UIThread.Post(() =>
        {
            _isCountUpdateQueued = false;
            ActionCountText.Text = string.Format(
                LocalizationManager.Instance.GetString("AutoClickRecordedCountFormat"),
                _pendingActionCount);
        });
    }

    public void FlashIndicator()
    {
        var now = DateTime.Now;
        if ((now - _lastFlashTime).TotalMilliseconds < 80) return;
        _lastFlashTime = now;

        Dispatcher.UIThread.Post(() =>
        {
            var border = this.FindControl<Border>("IndicatorBorder");
            if (border != null)
            {
                border.Opacity = 1;
                if (_isFlashResetQueued) return;
                _isFlashResetQueued = true;
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(160);
                    _isFlashResetQueued = false;
                    if (border.Parent != null) border.Opacity = 0.86;
                }, DispatcherPriority.Background);
            }
        });
    }
}
