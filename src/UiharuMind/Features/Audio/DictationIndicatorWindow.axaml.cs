using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Audio;

/// <summary>
/// 在别的应用里听写时屏幕下方的悬浮提示；不抢焦点、点不到（文字要粘进当时在前台的应用）。
/// 整个应用只建一扇、只显隐不关：macOS 上刚显示就关掉的窗口在销毁时会被 AppKit 中止进程（短按即开即关，实测撞到过）
/// </summary>
public partial class DictationIndicatorWindow : Window
{
    private const int BottomMargin = 96;

    /// <summary>构造窗口（经 <see cref="Attach"/> 按听写状态显隐）</summary>
    public DictationIndicatorWindow()
    {
        InitializeComponent();
        this.SetSimpledecorationPureWindow(isTopmost: true);
        this.SetNonInteractiveOverlayWindow();
        CanResize = false;
    }

    /// <summary>
    /// 跟着听写状态显隐提示：录给或识别给这个去向时显示，回到空闲就藏起来
    /// </summary>
    /// <param name="service">听写服务</param>
    /// <param name="target">别的应用那个去向</param>
    /// <returns>提示的数据（随应用存活）</returns>
    public static DictationIndicatorViewData Attach(IDictationService service, IDictationTarget target)
    {
        DictationIndicatorViewData data = new(service, target);
        DictationIndicatorWindow? window = null;
        data.ActiveChanged += () =>
        {
            if (!data.IsActive)
            {
                window?.Hide();
                return;
            }

            window ??= new DictationIndicatorWindow { DataContext = data };
            if (!window.IsVisible) window.Show();
            window.PlaceAtBottom();
            // 按内容定宽，量好之后再摆一次
            Dispatcher.UIThread.Post(window.PlaceAtBottom, DispatcherPriority.Loaded);
        };
        return data;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.SetNonInteractiveOverlayWindow(); //原生的点击穿透要等有了句柄才设得上
    }

    // 当前屏幕工作区的底部居中
    private void PlaceAtBottom()
    {
        Screen screen = App.ScreensService.GetSafeActivationScreen();
        double scaling = screen.Scaling;
        int width = (int)Math.Ceiling(Bounds.Width * scaling);
        int height = (int)Math.Ceiling(Bounds.Height * scaling);
        PixelRect area = screen.WorkingArea;
        Position = new PixelPoint(area.X + (area.Width - width) / 2,
            area.Bottom - height - (int)(BottomMargin * scaling));
    }
}
