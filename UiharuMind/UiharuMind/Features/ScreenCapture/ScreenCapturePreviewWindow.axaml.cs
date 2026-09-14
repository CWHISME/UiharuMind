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
using UiharuMind.Core.Input;

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

    private PixelVector _dragGrabOffset; //按下那一刻，全局鼠标相对窗口左上的偏移
    private bool _isDragging;
    private Size _originSize;
    private Size _maxDisplaySize = new(double.PositiveInfinity, double.PositiveInfinity);
    private double _aspectRatio = 1.0f;
    private double _currentScale = 1.0f;
    private Size _currentSize;
    private BitmapInterpolationMode? _zoomRestoreQuality;
    private int _zoomQualityGeneration;

    // Show 之前设的窗口尺寸会被 macOS 按 visibleFrame 裁掉（native Resize 的 !_shown 钳制）：
    // 竖着截一屏高时窗口被压矮、位置跟着上移，而 ImageContent 是 Stretch=Fill，于是图被压扁。
    // 所以先把真实几何记下来，Show 之后再原子提交一次（缩放那一路本来就是这么落盘的）
    private PixelPoint? _pendingFramePosition;
    private Size? _pendingFrameSize;

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
    }

    /// <summary>
    /// 当前图片的显示尺寸（不含阴影留白），与窗口尺寸差一圈 <see cref="ShadowMargin"/>。
    /// </summary>
    public Size DisplaySize => _currentSize;

    /// <inheritdoc />
    public event Action? DockAnchorChanged;

    /// <inheritdoc />
    /// <remarks>
    /// 拖动中按全局鼠标反推：系统拖动把窗口交给 WindowServer 搬，<see cref="Window.Position"/>
    /// 要 110ms 才同步一次（实测），拿它贴停靠栏就会明显落后于手。
    /// </remarks>
    public PixelPoint DockAnchorPosition =>
        _isDragging ? App.ScreensService.MousePosition - _dragGrabOffset : Position;

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
        _originSize = size ?? DefaultDisplaySize(image);
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

        if (pos == null) AlignImageToMouse(horizontalAlignment, verticalAlignment);

        // 见字段注释：Show 之前的尺寸可能被裁，落位推迟到 OnPostShow
        _pendingFramePosition = Position;
        _pendingFrameSize = ToWindowSize(_originSize);
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
        ApplyPendingFrame();
    }

    // Show 之后重新落一次几何，把 native 在 Show 那一刻按 visibleFrame 做的钳制顶回去
    private void ApplyPendingFrame()
    {
        if (_pendingFramePosition is not { } pos || _pendingFrameSize is not { } size) return;
        _pendingFramePosition = null;
        _pendingFrameSize = null;

        if (!this.TrySetWindowFrame(pos, size)) Position = pos;
        Width = size.Width;
        Height = size.Height;
    }

    // 让「图片」而不是「窗口」贴合截取范围。
    // SetWindowToMousePosition 对齐的是窗口边，而图片被 ShadowMargin 内缩了一圈透明死区，
    // 直接用窗口尺寸定位，图片就会朝拖动起点那侧偏掉一个 margin（往右下拖是左上偏，反向拖反向偏）。
    // 办法是按图片尺寸算出图片该在的位置，再把窗口左上角往回退一个左上留白
    private void AlignImageToMouse(HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment)
    {
        this.SetWindowToMousePosition(horizontalAlignment, verticalAlignment, _originSize.Width, _originSize.Height);

        double positionUnitsPerDip = DisplayUnits.PositionUnitsPerDip(App.ScreensService.Scaling, RenderScaling);
        Position -= new PixelVector(
            (int)Math.Round(ShadowMargin.Left * positionUnitsPerDip),
            (int)Math.Round(ShadowMargin.Top * positionUnitsPerDip));
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
        DockAnchorChanged?.Invoke();
    }

    // 位图一律 96 DPI（见 IScreenFrame.Display），显示尺寸只能由物理像素除以「每 DIP 多少像素」得到。
    // 这里必须走 DisplayUnits：mac 的 Screen.Scaling 恒为 1，直接拿它算会让 Retina 截图开出双倍大的窗
    private Size DefaultDisplaySize(Bitmap image)
    {
        double pixelsPerDip = DisplayUnits.PixelsPerDip(App.ScreensService.Scaling, RenderScaling);
        return image.PixelSize.ToSize(pixelsPerDip);
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

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        ScreenCaptureManager.SyncDockWindow(this);
    }
    
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
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
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            SafeClose(0.1f);
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 窗口本身交给系统拖动循环（mac 下是 performWindowDragWithEvent:），与其它窗口一致；
        // 手写位移在跨屏时会抖（窗内坐标按旧 frame 算，却要和已移动的 Position 相加，残差每帧回灌）。
        // 停靠栏跟随则不能再问窗口在哪，改用按下时记下的抓取偏移，按全局鼠标反推，见 DockAnchorPosition
        if (!App.ScreensService.IsMousePositionReliable) return;
        _dragGrabOffset = App.ScreensService.MousePosition - Position;
        BeginDrag();
        BeginMoveDrag(e);
    }

    // 系统拖动期间窗口照常收得到移动事件（只有 Position 是滞后的），跟随就挂在这里
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isDragging) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        DockAnchorChanged?.Invoke();
    }

    private void BeginDrag()
    {
        if (_isDragging) return;
        _isDragging = true;
        InputManager.Instance.EventOnMouseReleased += OnHookReleased;
    }

    // 拖动结束必须由全局钩子兜底：系统拖动中收不到 PointerReleased，
    // 而松手时鼠标往往已经在别的窗口上，本窗再也等不到下一次 PointerMoved。
    // 标记留着不清，DockAnchorPosition 就会一直按鼠标反推，停靠栏一悬上来便瞬移
    private void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        InputManager.Instance.EventOnMouseReleased -= OnHookReleased;
    }

    // 钩子线程回调，只做转发
    private void OnHookReleased(SharpHook.Data.MouseEventData data)
    {
        if (data.Button != SharpHook.Data.MouseButton.Button1) return;
        Dispatcher.UIThread.Post(EndDrag);
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
        EndDrag();
        ResetOcr();
        SafeSetImage(null);
    }

    public override void Hide()
    {
        base.Hide();
        EndDrag();
        ResetOcr();
        SafeSetImage(null);
    }
}