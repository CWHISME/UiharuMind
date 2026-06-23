using System;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Core.Configs;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.AutoClick;

public partial class PlaybackIndicatorWindow : UiharuWindowBase
{
    private int _currentRound = 0;
    private int _totalRounds = 1;
    private DateTime _lastFlashTime = DateTime.MinValue;
    private bool _isProgressUpdateQueued;
    private bool _isFlashResetQueued;

    public PlaybackIndicatorWindow()
    {
        InitializeComponent();

        this.SetSimpledecorationPureWindow(isTopmost: true);
        this.SetNonInteractiveOverlayWindow();
        Width = 220;
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

    public void UpdateProgress(int current, int total)
    {
        _currentRound = current;
        _totalRounds = total;
        if (_isProgressUpdateQueued) return;
        _isProgressUpdateQueued = true;

        Dispatcher.UIThread.Post(() =>
        {
            _isProgressUpdateQueued = false;
            RoundText.Text = _totalRounds > 0
                ? string.Format(LocalizationManager.Instance.GetString("AutoClickPlaybackRoundFormat"), _currentRound, _totalRounds)
                : string.Format(LocalizationManager.Instance.GetString("AutoClickPlaybackInfiniteRoundFormat"), _currentRound);
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
