/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Features.Desktop;

/// <summary>
/// 桌面扩展动手时在目标上闪一下的框（ADR 0084）：置顶、鼠标穿透、不抢焦点，约一秒后自己藏起。全应用一个
/// </summary>
public partial class DesktopHighlightWindow : UiharuWindowBase
{
    private const int VisibleMs = 900;
    private const int FrameMargin = 4; //框比目标大一圈，不压住目标本身的字

    private static DesktopHighlightWindow? _instance;
    private int _generation; //连着闪时只让最后一次去藏

    public DesktopHighlightWindow()
    {
        InitializeComponent();
        this.SetSimpledecorationPureWindow(isTopmost: true);
        this.SetNonInteractiveOverlayWindow();
        CanResize = false;
        ShowInTaskbar = false;
    }

    public override bool IsCacheWindow => false;
    public override bool ContributesToMacRegularMode => false;

    /// <summary>
    /// 挂到桌面宿主上：之后它每次动手都在这里闪一下
    /// </summary>
    public static void Attach() =>
        DesktopHost.Shared.Highlight = rect => Dispatcher.UIThread.Post(() => (_instance ??= new DesktopHighlightWindow()).Flash(rect));

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.SetNonInteractiveOverlayWindow();
    }

    // 平台给的是屏幕逻辑坐标：macOS 上窗口位置本就按点算，Windows 上窗口位置按物理像素、尺寸按缩放后的单位
    private void Flash(AxRect rect)
    {
        double scaling = PlatformUtils.IsMacOS ? 1 : App.ScreensService.Scaling;
        Position = new PixelPoint((int)(rect.X - FrameMargin), (int)(rect.Y - FrameMargin));
        Width = (rect.Width + FrameMargin * 2) / scaling;
        Height = (rect.Height + FrameMargin * 2) / scaling;
        if (!IsVisible) Show();

        int generation = Interlocked.Increment(ref _generation);
        _ = HideLaterAsync(generation);
    }

    private async Task HideLaterAsync(int generation)
    {
        await Task.Delay(VisibleMs);
        if (generation == _generation) Hide();
    }
}
