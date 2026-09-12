/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;

namespace UiharuMind.Features.ScreenCapture;

public partial class ScreenCapturePreviewWindow : UiharuWindowBase, IDockedWindow //Window, IDockedWindow
{
    public override bool ContributesToMacRegularMode => false;

    private Point _dragStartPoint;
    private bool _isDragging;
    private Size _originSize;
    // private double _minScale;


    // 截图尺寸的位图,本窗是它们的唯一所有者:关窗/隐藏即释放(见 SafeSetImage)。
    // 三个字段允许指向同一实例,释放前必须按引用去重。
    // 想把图交给活得比本窗久的东西(缓存窗、气泡),必须先 CloneBitmap 一份

    /// <summary>编辑前的原图，供「看改前/改后」来回切；与另两个字段可能是同一实例</summary>
    public Bitmap? ImageBackupSource;

    /// <summary>本窗刚被设进来的那一张；由外部编辑流程改写</summary>
    public Bitmap? ImageOriginSource;

    /// <summary>当前正显示的那一张；停靠栏的复制/保存/OCR 都借它，但不得释放</summary>
    public Bitmap? ImageSource;
    // public Bitmap? ImageNewSource;

    public ScreenCapturePreviewWindow()
    {
        InitializeComponent();

        //SizeToContent = SizeToContent.WidthAndHeight;

        // 必须 borderless：带 titled mask 的窗口会被 AppKit 框在标题栏可够到的范围，
        // setFrameTopLeftPoint 直接顶到 y=30 就不动了，永远盖不上菜单栏（遮罩是 None，不受影响）
        this.SetSimpledecorationPureWindow();
        ShowActivated = false;
        ShowInTaskbar = false;

        this.MinWidth = 50;
        this.MinHeight = 50;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChangedEvent;
        PointerEntered += OnMouseEnter;
        // PointerExited += OnMouseLeave;
    }

    // private const double MinScale = 0.20f;
    // private const double MaxScale = 12.0f;
    private const double ScaleStep = 0.1f;
    private const int ZoomQualityRestoreMs = 150;

    private double _aspectRatio = 1.0f;
    private double _currentScale = 1.0f;
    private Size _currentSize;
    private BitmapInterpolationMode? _zoomRestoreQuality;
    private int _zoomQualityGeneration;
    // private PixelPoint _currentPixelPoint;

    /// <summary>
    /// 显示一张图。<b>本窗接管这张位图</b>——关窗或隐藏时会释放它，调用方交出之后不要再用。
    /// 调用方还要继续用同一张图的，请自己 <c>CloneBitmap</c> 一份再交进来。
    /// </summary>
    /// <param name="image">要显示的图，所有权移交本窗</param>
    /// <param name="size">显示尺寸，默认取图片原始尺寸</param>
    /// <param name="pos">窗口位置，null 表示跟随鼠标</param>
    /// <param name="horizontalAlignment">相对鼠标的水平对齐</param>
    /// <param name="verticalAlignment">相对鼠标的垂直对齐</param>
    public void SetImage(Bitmap image, Size? size = null, PixelPoint? pos = null, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top)
    {
        // Content = new Image { Source = image };
        var scaling = App.ScreensService.Scaling;
        _originSize = size ?? DefaultDisplaySize(image, scaling);
        // _minScale = Math.Min(100.0 / _originSize.Width, 100.0 / _originSize.Height);
        // 计算原始尺寸的比例
        _aspectRatio = _originSize.Width / _originSize.Height;

        SafeSetImage(image);

        var bounds = App.ScreensService.MouseScreen?.Bounds;
        if (bounds != null)
        {
            MaxWidth = bounds.Value.Width / scaling * 2;
            MaxHeight = bounds.Value.Height / scaling * 2;
        }

        SetImageSize(_originSize);
        _currentScale = 1.0; // 换图后缩放归一，否则沿用旧 scale 下一次滚轮会跳变

        if (pos == null) this.SetWindowToMousePosition(horizontalAlignment, verticalAlignment, _originSize.Width, _originSize.Height);
    }

    protected override void OnInitWindowPosition()
    {
        // base.OnInitWindowPosition();
    }

    protected override void OnPostShow()
    {
        base.OnPostShow();
        // 钉图也要能拖到菜单栏上面去：只抬层级，不换 Space 归属
        OverlayWindowService.ApplyNativePinAboveMenuBarStyle(this);
    }

    private void SetImageSize(Size newSize)
    {
        _currentSize = newSize;
        this.Width = newSize.Width;
        this.Height = newSize.Height;
        // ClientSize = newSize;
    }

    // 自带 DPI 的位图（如 mac 2x 抓屏，Dpi=192）按 Size（point）显示；
    // 无 DPI 信息的一律 96，走像素除缩放的老逻辑
    private static Size DefaultDisplaySize(Bitmap image, double scaling)
    {
        var dpi = image.Dpi;
        if (Math.Abs(dpi.X - 96) > 0.01 || Math.Abs(dpi.Y - 96) > 0.01) return image.Size;
        return image.PixelSize.ToSize(scaling);
    }

    private void SafeSetImage(Bitmap? image)
    {
        // 三个字段经常指向同一个实例:本方法自己就把 ImageSource 与 ImageOriginSource 设成同一张,
        // 编辑回来那一路还会把 ImageOriginSource 与 ImageBackupSource 也设成同一张。
        // 所以必须按引用去重(Bitmap 不重写 Equals,Distinct 即按引用比),否则同一张会被 Dispose 两三次:
        // Avalonia 位图是引用计数的,多减一次就可能在渲染线程还持有它时把底层表面放掉。
        // 新图本身也要排除——调用方可能把本窗已经持有的那张又传了回来
        Bitmap[] stale = new[] { ImageBackupSource, ImageSource, ImageOriginSource }
            .Where(x => x != null && !ReferenceEquals(x, image))
            .Select(x => x!)
            .Distinct()
            .ToArray();

        // 先把新值挂上界面、再释放旧值。反过来做的话已释放的位图还挂在 Image.Source 上,下一帧渲染就撞上去
        ImageContent.Source = null;
        ImageBackupSource = null;
        ImageSource = image;
        ImageOriginSource = image;
        ImageContent.Source = image;

        foreach (Bitmap old in stale) old.Dispose();
    }

    private void OnMouseEnter(object? sender, PointerEventArgs e)
    {
        ScreenCaptureManager.SyncDockWindow(this);
    }

    // private void OnMouseLeave(object? sender, PointerEventArgs e)
    // {
    //     ScreenCaptureManager.SyncBreakDockWindow(this);
    // }

    private void OnPointerWheelChangedEvent(object? sender, PointerWheelEventArgs e)
    {
        var mousePosition = e.GetPosition(ImageContent);
        var curPos = Position;

        if (e.Delta.Y != 0)
        {
            // 计算新的缩放比例
            var newScale = _currentScale * (1 + e.Delta.Y * ScaleStep);

            // 上下限沿用窗口 Min/Max，只收敛到这一处钳制
            double minScale = Math.Min(MinWidth / _originSize.Width, MinHeight / _originSize.Height);
            double maxScale = Math.Min(MaxWidth / _originSize.Width, MaxHeight / _originSize.Height);
            if (double.IsFinite(maxScale)) newScale = Math.Min(newScale, maxScale);
            newScale = Math.Max(newScale, minScale);
            if (Math.Abs(newScale - _currentScale) < 0.001) return;

            var newSize =
                _originSize.ScaleByWidth(newScale, _aspectRatio, MinWidth, MinHeight, MaxWidth, MaxHeight);
            if (_currentSize.Width <= 0 || _currentSize.Height <= 0) return;
            if (newSize.Width <= 0 || newSize.Height <= 0) return;

            // 光标锚定： trunc 改 Round，收敛只做一次；
            // Position 与窗内偏移的单位换算收敛到 DisplayUnits（mac 全是 point，Windows 差一个屏缩放）
            double positionUnitsPerDip = DisplayUnits.PositionUnitsPerDip(App.ScreensService.Scaling, RenderScaling);
            double zoomX = newSize.Width / _currentSize.Width;
            double zoomY = newSize.Height / _currentSize.Height;

            //调整窗口位置
            int newPosX = (int)Math.Round(curPos.X - (mousePosition.X * positionUnitsPerDip * (zoomX - 1)));
            int newPosY = (int)Math.Round(curPos.Y - (mousePosition.Y * positionUnitsPerDip * (zoomY - 1)));

            var pos = new PixelPoint(newPosX, newPosY);

            //确保鼠标位置在缩放后不超出界面
            pos += UiUtils.EnsureMousePositionWithinTargetOffset(pos, newSize);

            // 以钳制后的实际尺寸为准存 scale，否则顶到上下限时两者脱钩，往回滚会先卡住再跳变
            _currentScale = newSize.Width / _originSize.Width;
            MarkZoomInteractive();

            // macOS 原子提交：位置与尺寸一次 setFrame 落盘，不再分两帧撕裂；
            // 失败或非 macOS 才走托管老路
            if (this.TrySetWindowFrame(pos, newSize))
            {
                SetImageSize(newSize);
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    this.Position = pos;
                    SetImageSize(newSize);
                }, DispatcherPriority.MaxValue);
            }

            e.Handled = true;
        }
    }

    // 手势期间降为低质量重采样，停稳后恢复原档，避免逐帧高质量重采样拖慢 UI 线程
    private void MarkZoomInteractive()
    {
        _zoomRestoreQuality ??= RenderOptions.GetBitmapInterpolationMode(ImageContent);
        if (RenderOptions.GetBitmapInterpolationMode(ImageContent) != BitmapInterpolationMode.LowQuality)
            RenderOptions.SetBitmapInterpolationMode(ImageContent, BitmapInterpolationMode.LowQuality);
        int generation = ++_zoomQualityGeneration;
        DispatcherTimer.RunOnce(() =>
        {
            if (generation == _zoomQualityGeneration && _zoomRestoreQuality.HasValue)
                RenderOptions.SetBitmapInterpolationMode(ImageContent, _zoomRestoreQuality.Value);
        }, TimeSpan.FromMilliseconds(ZoomQualityRestoreMs), DispatcherPriority.Background);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // ScreenCaptureManager.SyncDockWindow(null);
            SafeClose(0.1f);
            // Close();
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _isDragging = true;
            // 抓住指针：拖进菜单栏后移动事件不再按命中路由，没有捕获窗会冻在菜单栏边缘
            e.Pointer.Capture(this);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            var position = e.GetPosition(this);
            var diff = position - _dragStartPoint;

            // 窗口位移是 Position 口径（mac point，Windows 像素），DIP 差值要换算
            double unitsPerDip = DisplayUnits.PositionUnitsPerDip(App.ScreensService.Scaling, RenderScaling);
            var windowPosition = this.Position;
            windowPosition = new PixelPoint(
                (int)Math.Round(windowPosition.X + diff.X * unitsPerDip),
                (int)Math.Round(windowPosition.Y + diff.Y * unitsPerDip)
            );
            // Log.Debug($"windowPosition: {windowPosition}");
            this.Position = windowPosition;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SafeSetImage(null);
    }

    public override void Hide()
    {
        base.Hide();
        SafeSetImage(null);
    }
}
