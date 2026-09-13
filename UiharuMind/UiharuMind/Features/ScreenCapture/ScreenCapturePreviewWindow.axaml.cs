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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture.Ocr;

namespace UiharuMind.Features.ScreenCapture;

public partial class ScreenCapturePreviewWindow : UiharuWindowBase, IDockedWindow
{
    // 阴影留白：BoxShadow 画在 Border 之外，窗口不留出这一圈就会被窗口边界裁掉（看不见阴影）。
    // 代价是贴图四周多出一圈透明的死区，点不到底下的应用——数值只够放下阴影，不再多留
    private static readonly Thickness ShadowMargin = new(12, 8, 12, 16);

    private const double MinDisplayLength = 50;
    private const double ScaleStep = 0.1f;
    private const int ZoomQualityRestoreMs = 150;

    public override bool ContributesToMacRegularMode => false;

    private Point _dragStartPoint;
    private bool _isDragging;
    private Size _originSize;
    private Size _maxDisplaySize = new(double.PositiveInfinity, double.PositiveInfinity);
    private double _aspectRatio = 1.0f;
    private double _currentScale = 1.0f;
    private Size _currentSize;
    private BitmapInterpolationMode? _zoomRestoreQuality;
    private int _zoomQualityGeneration;

    // OCR：本窗只管开关、跑识别、把行交给选择层；选择/复制/右键菜单都在层里
    private readonly IOcrTextRecognizer _ocrRecognizer = OcrRecognizerFactory.Create();
    private bool _ocrMode;
    private int _ocrGeneration;

    // 截图尺寸的位图,本窗是它们的唯一所有者:关窗/隐藏即释放(见 SafeSetImage)。
    // 三个字段允许指向同一实例,释放前必须按引用去重。
    // 想把图交给活得比本窗久的东西(缓存窗、气泡),必须先 CloneBitmap 一份

    /// <summary>编辑前的原图，供「看改前/改后」来回切；与另两个字段可能是同一实例</summary>
    public Bitmap? ImageBackupSource;

    /// <summary>本窗刚被设进来的那一张；由外部编辑流程改写</summary>
    public Bitmap? ImageOriginSource;

    /// <summary>当前正显示的那一张；停靠栏的复制/保存/OCR 都借它，但不得释放</summary>
    public Bitmap? ImageSource;

    public ScreenCapturePreviewWindow()
    {
        InitializeComponent();

        FrameBorder.Margin = ShadowMargin;
        FrameBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            Color = Color.FromArgb(0x80, 0, 0, 0),
            Blur = 18,
            Spread = 1,
            OffsetX = 0,
            OffsetY = 6
        });

        // 必须 borderless：带 titled mask 的窗口会被 AppKit 框在标题栏可够到的范围，
        // setFrameTopLeftPoint 直接顶到 y=30 就不动了，永远盖不上菜单栏（遮罩是 None，不受影响）
        this.SetSimpledecorationPureWindow();
        ShowActivated = false;
        ShowInTaskbar = false;

        MinWidth = MinDisplayLength + ShadowMargin.Left + ShadowMargin.Right;
        MinHeight = MinDisplayLength + ShadowMargin.Top + ShadowMargin.Bottom;

        OcrLayer.CopyRequested += CopyOcrText;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChangedEvent;
        PointerEntered += OnMouseEnter;
    }

    /// <summary>
    /// 当前图片的显示尺寸（不含阴影留白），与窗口尺寸差一圈 <see cref="ShadowMargin"/>。
    /// </summary>
    public Size DisplaySize => _currentSize;

    /// <inheritdoc />
    public Rect DockAnchorBounds => new(ShadowMargin.Left, ShadowMargin.Top, _currentSize.Width, _currentSize.Height);

    /// <summary>
    /// OCR 文字选择模式是否打开（Dock 工具条的开关读这个状态回显）。
    /// </summary>
    public bool OcrMode => _ocrMode;

    /// <summary>
    /// 当前平台是否有可用的系统 OCR。Dock 工具条用它决定显不显示入口。
    /// </summary>
    public static bool OcrSupported => OcrRecognizerFactory.IsSupported;

    /// <summary>
    /// 显示一张图。<b>本窗接管这张位图</b>——关窗或隐藏时会释放它，调用方交出之后不要再用。
    /// 调用方还要继续用同一张图的，请自己 <c>CloneBitmap</c> 一份再交进来。
    /// </summary>
    /// <param name="image">要显示的图，所有权移交本窗</param>
    /// <param name="size">显示尺寸，默认取图片原始尺寸</param>
    /// <param name="pos">窗口位置，null 表示跟随鼠标</param>
    /// <param name="horizontalAlignment">相对鼠标的水平对齐</param>
    /// <param name="verticalAlignment">相对鼠标的垂直对齐</param>
    public void SetImage(Bitmap image, Size? size = null, PixelPoint? pos = null,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top)
    {
        var scaling = App.ScreensService.Scaling;
        _originSize = size ?? DefaultDisplaySize(image, scaling);
        // 计算原始尺寸的比例
        _aspectRatio = _originSize.Width / _originSize.Height;

        SafeSetImage(image);

        var bounds = App.ScreensService.MouseScreen?.Bounds;
        if (bounds != null)
        {
            _maxDisplaySize = new Size(bounds.Value.Width / scaling * 2, bounds.Value.Height / scaling * 2);
            var maxWindow = ToWindowSize(_maxDisplaySize);
            MaxWidth = maxWindow.Width;
            MaxHeight = maxWindow.Height;
        }

        SetDisplaySize(_originSize);
        _currentScale = 1.0; // 换图后缩放归一，否则沿用旧 scale 下一次滚轮会跳变

        if (pos == null)
        {
            var windowSize = ToWindowSize(_originSize);
            this.SetWindowToMousePosition(horizontalAlignment, verticalAlignment, windowSize.Width, windowSize.Height);
        }
    }

    protected override void OnInitWindowPosition()
    {
        // base.OnInitWindowPosition();
    }

    protected override void OnPostShow()
    {
        base.OnPostShow();
        // 钉图也要能拖到菜单栏上面去：只抬层级，不换 Space 归属
        OverlayWindowService.ApplyNativeWindowLevel(this, EOverlayWindowLevel.Pinned);
    }

    /// <summary>
    /// 图片显示尺寸 → 窗口尺寸：外面多一圈给阴影用的透明留白。
    /// </summary>
    private static Size ToWindowSize(Size displaySize) => new(
        displaySize.Width + ShadowMargin.Left + ShadowMargin.Right,
        displaySize.Height + ShadowMargin.Top + ShadowMargin.Bottom);

    private void SetDisplaySize(Size newSize)
    {
        _currentSize = newSize;
        var windowSize = ToWindowSize(newSize);
        Width = windowSize.Width;
        Height = windowSize.Height;
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

        // 换图后旧行盒失效；框选态则对新图重跑一遍
        ResetOcr();
        if (_ocrMode && image != null) BeginRecognize();
    }

    private void OnMouseEnter(object? sender, PointerEventArgs e)
    {
        ScreenCaptureManager.SyncDockWindow(this);
    }

    private void OnPointerWheelChangedEvent(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;

        var mousePosition = e.GetPosition(ImageContent);
        var curPos = Position;

        // 计算新的缩放比例；上下限收敛到显示尺寸这一处钳制
        var newScale = _currentScale * (1 + e.Delta.Y * ScaleStep);
        double minScale = Math.Min(MinDisplayLength / _originSize.Width, MinDisplayLength / _originSize.Height);
        double maxScale = Math.Min(_maxDisplaySize.Width / _originSize.Width,
            _maxDisplaySize.Height / _originSize.Height);
        if (double.IsFinite(maxScale)) newScale = Math.Min(newScale, maxScale);
        newScale = Math.Max(newScale, minScale);
        if (Math.Abs(newScale - _currentScale) < 0.001) return;

        var newSize = _originSize.ScaleByWidth(newScale, _aspectRatio, MinDisplayLength, MinDisplayLength,
            _maxDisplaySize.Width, _maxDisplaySize.Height);
        if (_currentSize.Width <= 0 || _currentSize.Height <= 0) return;
        if (newSize.Width <= 0 || newSize.Height <= 0) return;

        // 光标锚定： trunc 改 Round，收敛只做一次；
        // Position 与窗内偏移的单位换算收敛到 DisplayUnits（mac 全是 point，Windows 差一个屏缩放）
        double positionUnitsPerDip = DisplayUnits.PositionUnitsPerDip(App.ScreensService.Scaling, RenderScaling);
        double zoomX = newSize.Width / _currentSize.Width;
        double zoomY = newSize.Height / _currentSize.Height;

        //调整窗口位置（阴影留白是常量，窗口位移与图片位移一一对应）
        int newPosX = (int)Math.Round(curPos.X - (mousePosition.X * positionUnitsPerDip * (zoomX - 1)));
        int newPosY = (int)Math.Round(curPos.Y - (mousePosition.Y * positionUnitsPerDip * (zoomY - 1)));

        var pos = new PixelPoint(newPosX, newPosY);
        var newWindowSize = ToWindowSize(newSize);

        //确保鼠标位置在缩放后不超出界面
        pos += UiUtils.EnsureMousePositionWithinTargetOffset(pos, newWindowSize);

        // 以钳制后的实际尺寸为准存 scale，否则顶到上下限时两者脱钩，往回滚会先卡住再跳变
        _currentScale = newSize.Width / _originSize.Width;
        MarkZoomInteractive();

        // macOS 原子提交：位置与尺寸一次 setFrame 落盘，不再分两帧撕裂；
        // 失败或非 macOS 才走托管老路
        if (this.TrySetWindowFrame(pos, newWindowSize))
        {
            SetDisplaySize(newSize);
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Position = pos;
                SetDisplaySize(newSize);
            }, DispatcherPriority.MaxValue);
        }

        e.Handled = true;
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

    // 落在 OCR 文字行上的按下已被选择层吃掉（Handled），到不了这里，拖窗因此不会和选字打架
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            SafeClose(0.1f);
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
        if (!_isDragging) return;

        var position = e.GetPosition(this);
        var diff = position - _dragStartPoint;

        // 窗口位移是 Position 口径（mac point，Windows 像素），DIP 差值要换算
        double unitsPerDip = DisplayUnits.PositionUnitsPerDip(App.ScreensService.Scaling, RenderScaling);
        var windowPosition = Position;
        Position = new PixelPoint(
            (int)Math.Round(windowPosition.X + diff.X * unitsPerDip),
            (int)Math.Round(windowPosition.Y + diff.Y * unitsPerDip));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>
    /// 开关 OCR 文字选择。打开时把当前图送识别器跑一遍，出结果后即可在图上逐字选取。
    /// </summary>
    /// <param name="on">打开还是关闭</param>
    public void SetOcrMode(bool on)
    {
        if (on == _ocrMode) return;
        _ocrMode = on;
        ResetOcr();
        if (on) BeginRecognize();
    }

    // 作废进行中的识别并收掉界面上的 OCR 痕迹
    private void ResetOcr()
    {
        _ocrGeneration++;
        OcrLayer.Clear();
        OcrLoadingBar.IsVisible = false;
    }

    private void BeginRecognize()
    {
        if (ImageSource == null || !OcrSupported) return;
        ShowOcrTip(Lang.PreviewOcr_Recognizing, autoHide: false);
        _ = RunOcrAsync(_ocrGeneration, ImageSource);
    }

    private async Task RunOcrAsync(int generation, Bitmap source)
    {
        try
        {
            var lines = await _ocrRecognizer.RecognizeAsync(source).ConfigureAwait(true);
            if (generation != _ocrGeneration || !_ocrMode) return;

            OcrLoadingBar.IsVisible = false;
            if (lines.Count == 0)
            {
                ShowOcrTip(Lang.PreviewOcr_NoText, autoHide: true);
                return;
            }

            OcrLayer.SetLines(lines);
        }
        catch (Exception e)
        {
            Log.Warning($"OCR 文字选择失败：{e.Message}");
            if (generation == _ocrGeneration) OcrLoadingBar.IsVisible = false;
        }
    }

    private void ShowOcrTip(string text, bool autoHide)
    {
        OcrLoadingText.Text = text;
        OcrLoadingBar.IsVisible = true;
        if (!autoHide) return;

        int generation = _ocrGeneration;
        DispatcherTimer.RunOnce(() =>
        {
            if (generation == _ocrGeneration) OcrLoadingBar.IsVisible = false;
        }, TimeSpan.FromMilliseconds(1500));
    }

    // 复制成功后剪贴板事件会照常触发，浮动快捷工具（QuickToolWindow）按原链路接手
    private void CopyOcrText(string text)
    {
        try
        {
            App.Clipboard.CopyToClipboard(text);
        }
        catch (Exception e)
        {
            Log.Warning($"复制选中文字失败：{e.Message}");
            return;
        }

        string preview = text.Length > 42 ? text[..42] + "…" : text.Replace("\n", " ");
        App.Services.GetRequiredService<IMessageService>()
            .ShowNotification($"{Lang.PreviewOcr_Copied}：{preview}", severity: MessageSeverity.Success);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ResetOcr();
        SafeSetImage(null);
    }

    public override void Hide()
    {
        base.Hide();
        ResetOcr();
        SafeSetImage(null);
    }
}
