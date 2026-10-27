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
using UiharuMind.Generated;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Services.Native;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture.Ocr;
using UiharuMind.Features.ScreenCapture.Drawing;
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

    // 缩放锚点的手势记忆：窗口位置只能落在整像素上，若每格都从「已经取整过的位置」反推锚点，
    // 那点亚像素误差会一格一格随机游走，连滚十几格就是肉眼可见的漂。手势期间锁住比例即可。
    private const int ZoomAnchorIdleMs = 400;
    private const int ZoomAnchorCursorSlack = 2;

    private Point _zoomAnchor = new(0.5, 0.5);
    private PixelPoint _zoomAnchorCursor;
    private long _zoomAnchorTickMs; // 0 表示还没采过样，第一格必然重采样（避免与 MinValue 相减溢出）

    // 滚轮只在事件里累积，几何统一在下一帧的 CommitZoom 里落盘：一帧多次滚轮合并成一次提交，
    // 且让窗口几何与内容布局落在同一帧（否则会出现「位置已跳、尺寸还是旧的」那一帧）
    private double? _pendingZoomScale;
    private bool _zoomCommitQueued;
    private Point _wheelImagePoint; // 最近一次滚轮时刻的光标（相对 ImageContent），仅作全局光标不可用时的兜底


    public override bool ContributesToMacRegularMode => false;

    public override EOverlayWindowLevel OverlayWindowLevel => EOverlayWindowLevel.Pinned;

    private PixelVector _dragGrabOffset; //按下那一刻，全局鼠标相对窗口左上的偏移
    private bool _isDragging;

    // Windows 自绘拖动：系统拖动循环不许窗口离开屏幕，只能自己按全局光标搬
    private Point _dragOffsetDip; // 同上的偏移，但存 DIP —— 跨屏 DPI 变了按新倍率换算才不错位
    private bool _managedDrag;
    private bool _dragCommitQueued;
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

    // 编辑模式（复用 ImageAnnotationEditor 在贴图窗上就地标注）：
    // 编辑器不接管位图；进入时备份当前图，保存后新合成图按 SafeSetImage 规则接管、原图转备份
    private bool _editMode;
    private Bitmap? _editBackup; //进入编辑前那张，保存后作为 ImageBackupSource 续命
    private DrawingTool _editTool = DrawingTool.Rectangle;
    private Color _editColor = Colors.Red;

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

    /// <summary>
    /// 当前显示缩放（原始尺寸 → 显示尺寸），进入编辑模式时传给编辑器做初始视图。
    /// </summary>
    public double DisplayScale => _currentScale;

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

    /// <summary>当前是否处于编辑模式（停靠窗按它切换工具条）</summary>
    public bool EditMode => _editMode;

    /// <summary>编辑模式切换（停靠窗订阅，切换停靠工具条）</summary>
    public event Action<bool>? EditModeChanged;

    /// <summary>编辑撤销/重做可用性变化（停靠窗刷新工具条按钮）</summary>
    public event Action? EditUndoRedoChanged;

    /// <summary>当前编辑工具（停靠窗进入编辑态时回显）</summary>
    public DrawingTool EditTool => _editTool;

    /// <summary>当前编辑颜色（停靠窗进入编辑态时回显）</summary>
    public Color EditColor => _editColor;

    public bool EditCanUndo => EditEditor.CanUndo;

    public bool EditCanRedo => EditEditor.CanRedo;

    /// <summary>
    /// 进入编辑模式：编辑器叠加在当前图上（初始视图 = 贴图窗当前缩放），
    /// 不改动本窗位置与尺寸。位图所有权不变，仍是本窗持有。
    /// </summary>
    public void EnterEditMode()
    {
        if (_editMode || ImageSource == null) return;
        _editMode = true;
        _editBackup = ImageSource;
        SetOcrMode(false); // 互斥：编辑器接管绘制，OCR 层先让位
        EditEditor.SetSource(ImageSource, DisplayScale);
        EditEditor.SetTool(_editTool);
        EditEditor.SetColor(_editColor);
        EditEditor.AllowViewZoom = false; // 编辑过程视图固定，画布坐标与视图比例不再变动
        EditEditor.UndoRedoChanged += OnEditUndoRedoChanged;
        EditEditor.IsVisible = true;
        EditModeChanged?.Invoke(true);
        OnEditUndoRedoChanged();
    }

    /// <summary>
    /// 取消编辑：丢弃标注，回到浏览模式。位图与几何均不动。
    /// </summary>
    public void CancelEditMode()
    {
        if (!_editMode) return;
        ExitEditModeCore();
    }

    /// <summary>
    /// 保存编辑：导出合成图替换当前图，回到浏览模式。
    /// 合成图经 <see cref="SetImage"/> 接管；编辑前那张转 ImageBackupSource（供「改前/改后」切换）。
    /// 进入编辑后没有任何标注（含画了又全撤销）时按没编辑处理：不换图、不进剪贴板/历史。
    /// </summary>
    public void SaveEditMode()
    {
        if (!_editMode) return;
        // 先渲染：输入中的文字会在这里落栈，否则只打了字没点别处会被误判成没改
        Bitmap combined = EditEditor.RenderToBitmap();
        bool modified = EditEditor.CanUndo; // 渲染后命令栈里还有没有真实标注
        Bitmap? backup = _editBackup;

        ExitEditModeCore();

        if (!modified)
        {
            combined.Dispose(); // 白渲的合成图没接住，随手释放
            return;
        }

        // 换图前先摘掉三字段引用：旧图由 backup 续命，否则 SafeSetImage 会把它当旧值释放；
        // 合成图经 SetImage 接管（位置、缩放与编辑前一致，见 scale 参数）
        ImageSource = null;
        ImageOriginSource = null;
        ImageBackupSource = null;
        SetImage(combined, pos: Position, scale: DisplayScale);
        ImageOriginSource = backup;
        ImageBackupSource = backup;

        // 沿用旧编辑流程的收尾：合成图进剪贴板与历史（剪贴板要独立一份，见 ClipboardService 注释）
        Bitmap? forClipboard = combined.CloneBitmap();
        if (forClipboard != null) App.Clipboard.CopyImageToClipboard(forClipboard, true);
        App.Clipboard.RecordImageToHistory(combined);
    }

    /// <summary>换编辑工具（停靠窗工具条转发）</summary>
    public void SetEditTool(DrawingTool tool)
    {
        _editTool = tool;
        if (_editMode) EditEditor.SetTool(tool);
    }

    /// <summary>换编辑颜色（停靠窗工具条转发）</summary>
    public void SetEditColor(Color color)
    {
        _editColor = color;
        if (_editMode) EditEditor.SetColor(color);
    }

    /// <summary>撤销（停靠窗工具条转发）</summary>
    public void EditUndo() => EditEditor.Undo();

    /// <summary>重做（停靠窗工具条转发）</summary>
    public void EditRedo() => EditEditor.Redo();

    private void ExitEditModeCore()
    {
        _editMode = false;
        EditEditor.UndoRedoChanged -= OnEditUndoRedoChanged;
        EditEditor.IsVisible = false;
        EditEditor.ClearSource();
        EditModeChanged?.Invoke(false);
    }

    private void OnEditUndoRedoChanged() => EditUndoRedoChanged?.Invoke();

    // 编辑态的撤销/重做快捷键（旧独立编辑窗的 KeyBindings 随窗口删除，这里补回）：
    // 输入框自身处理的按键优先（e.Handled），否则按标注命令栈撤销/重做
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_editMode && !e.Handled)
        {
            bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (command && e.Key == Key.Z)
            {
                EditUndo();
                e.Handled = true;
                return;
            }

            if (command && e.Key == Key.Y)
            {
                EditRedo();
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

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
        VerticalAlignment verticalAlignment = VerticalAlignment.Top,
        PixelPoint? anchorMouse = null,
        double? scale = null)
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

        if (scale is { } restoreScale && restoreScale > 0)
        {
            // 编辑返回：恢复进入编辑前的缩放状态（编辑窗继承了这个 scale，返回不应还原到原始大小）
            _currentScale = restoreScale;
            SetDisplaySize(_originSize.ScaleByWidth(restoreScale, _aspectRatio, MinDisplayLength, MinDisplayLength,
                _maxDisplaySize.Width, _maxDisplaySize.Height));
        }
        else
        {
            SetDisplaySize(_originSize);
            _currentScale = 1.0; // 换图后缩放归一，否则沿用旧 scale 下一次滚轮会跳变
        }

        if (pos == null) AlignImageToMouse(horizontalAlignment, verticalAlignment, anchorMouse);

        // 见字段注释：Show 之前的尺寸可能被裁，落位推迟到 OnPostShow
        _pendingFramePosition = Position;
        _pendingFrameSize = ToWindowSize(_currentSize);
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

        // 落盘尺寸以 native 带回的为准（Windows 上会对齐到托管 resize 能保持的整物理像素），
        // 记账与 Width/Height 都从它派生，才不会二次改动
        if (!this.TrySetWindowFrame(pos, size, out Size applied)) Position = pos;
        ApplyWindowSize(applied);
    }

    // 让「图片」而不是「窗口」贴合截取范围。
    // SetWindowToMousePosition 对齐的是窗口边，而图片被 ShadowMargin 内缩了一圈透明死区，
    // 直接用窗口尺寸定位，图片就会朝拖动起点那侧偏掉一个 margin（往右下拖是左上偏，反向拖反向偏）。
    // 办法是按图片尺寸算出图片该在的位置，再把窗口左上角往回退一个左上留白
    private void AlignImageToMouse(HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment,
        PixelPoint? anchor = null)
    {
        this.SetWindowToMousePosition(horizontalAlignment, verticalAlignment, _originSize.Width, _originSize.Height,
            anchor: anchor);

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

    /// <summary>
    /// 窗口尺寸 → 图片显示尺寸，<see cref="ToWindowSize"/> 的逆运算。
    /// 原生提交拿到的是窗口尺寸，记账用的是图片尺寸，两个方向各算一次、别只算一头：
    /// 只往一个方向补留白，窗口就会每滚一格凭空长一圈留白。
    /// </summary>
    private static Size ToDisplaySize(Size windowSize) => new(
        windowSize.Width - ShadowMargin.Left - ShadowMargin.Right,
        windowSize.Height - ShadowMargin.Top - ShadowMargin.Bottom);

    private void SetDisplaySize(Size newSize)
    {
        // 记账与 Width/Height 一律以「窗口尺寸」为账本：先对齐到窗口真实能落的网格
        // （Windows 是托管 resize 能保持的整物理像素，幂等），再由窗口尺寸反推图片尺寸
        var windowSize = DisplayUnits.SnapToPixels(ToWindowSize(newSize), DisplayUnits.WindowPixelsPerDip(this));
        ApplyWindowSize(windowSize);
    }

    /// <summary>
    /// 以<b>窗口尺寸</b>为唯一账本落一次几何记账：<see cref="_currentSize"/>、Width/Height 都从它派生。
    /// <para>
    /// 必须整窗一份账：Width/Height 与原生落盘用同一个 double 时，托管 resize 才会算出同一个像素尺寸、
    /// 走「client 尺寸已相等」分支直接返回；只往一个方向补留白或二次换算，窗口就会每滚一格改两回尺寸。
    /// </para>
    /// </summary>
    /// <param name="windowSize">窗口尺寸（DIP，含阴影留白），以原生落盘的为准</param>
    private void ApplyWindowSize(Size windowSize)
    {
        _currentSize = ToDisplaySize(windowSize);
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
        if (_editMode) return; // 编辑态缩放交给编辑器（AllowViewZoom=false 已禁滚轮缩放），本窗不再动几何
        if (e.Delta.Y == 0) return;

        // 这里只累积目标比例，真正的几何落盘推到下一帧（见 CommitZoom）。
        // 一帧内连来几个滚轮事件就合并成一次提交，不会每个事件各落一次几何
        _wheelImagePoint = e.GetPosition(ImageContent);
        double baseScale = _pendingZoomScale ?? _currentScale;
        _pendingZoomScale = ClampZoomScale(baseScale * (1 + e.Delta.Y * ScaleStep));
        e.Handled = true;

        if (_zoomCommitQueued) return;
        _zoomCommitQueued = true;
        // 排到 Input：与同一帧的输入批一起落盘，一帧内连来的几个滚轮事件先合并成一次提交
        Dispatcher.UIThread.Post(CommitZoom, DispatcherPriority.Input);
    }

    /// <summary>
    /// 把累积的缩放比例落盘成「先尺寸、再呈现、最后移动」三拍。
    /// <para>
    /// 不能合并成一次 <c>SetWindowPos</c>：原生几何是 DWM 立刻生效的，surface 内容却要等一次绘制。
    /// 两者一起提交就会有一帧「窗口已到新位置、图还是旧尺寸」——偏移 = 锚点 × 尺寸差
    /// （500px 图滚一格、光标居中约 25px），下一帧再弹回去，肉眼就是「跳过去又跳回来」。
    /// </para>
    /// <para>
    /// 拆开之后：① 只改尺寸、位置不动，那一帧最多是「内容晚一拍」（实测不抖）；
    /// ② 把这一帧画完让 surface 就位；③ 再纯移动——移动一块已就位的内容不留痕（拖动一直是这个机制）。
    /// 第 ② 步没赶上也不过多一次「不流畅」，不会退化成位置跳。
    /// </para>
    /// </summary>
    private void CommitZoom()
    {
        _zoomCommitQueued = false;
        if (_pendingZoomScale is not { } newScale) return;
        _pendingZoomScale = null;
        if (!IsVisible) return;
        if (_currentSize.Width <= 0 || _currentSize.Height <= 0) return;
        if (Math.Abs(newScale - _currentScale) < 0.001) return;

        var newSize = _originSize.ScaleByWidth(newScale, _aspectRatio, MinDisplayLength, MinDisplayLength,
            _maxDisplaySize.Width, _maxDisplaySize.Height);
        if (newSize.Width <= 0 || newSize.Height <= 0) return;

        // 尺寸先对齐到窗口真实能落的网格：比率、锚点、尺寸记账三者必须同一个值。
        // 否则「我们算的 DIP」与「窗口真实落的像素」差那一点，逐格缩放会把它当残差累积（见 DisplayUnits.SnapToPixels）
        double pixelsPerDip = DisplayUnits.WindowPixelsPerDip(this);
        var newWindowSize = DisplayUnits.SnapToPixels(ToWindowSize(newSize), pixelsPerDip);
        var newDisplaySize = ToDisplaySize(newWindowSize);

        // 顶到缩放上限时，SnapToPixels 的向上取整会越过 SetImage 里设的 MaxWidth/MaxHeight，
        // 布局随后把窗口钳回来 → 每格多一次 SetWindowPos（顶格处的 1px 回抽）。
        // 把 Max 抬到这次真正落盘的尺寸，尺寸就只由缩放这一路说了算（缩放上限本身由 _maxDisplaySize 卡）
        MaxWidth = Math.Max(MaxWidth, newWindowSize.Width);
        MaxHeight = Math.Max(MaxHeight, newWindowSize.Height);

        // 光标锚定：位置一律由「光标 screen 坐标 + 归一化锚点」现算，不复用上一格的结果。
        // 这样每格的误差都只来自最后一次取整（≤0.5px），不会逐格累积成漂移。
        // 换算细节见 ZoomAnchorMath：留白只加不乘，锚点用比例而不是 DIP 偏移。
        double positionUnitsPerDip = DisplayUnits.WindowPositionUnitsPerDip(this);
        PixelPoint cursor = ResolveZoomCursor(positionUnitsPerDip);
        Point anchor = ResolveZoomAnchor(cursor, Position, positionUnitsPerDip);
        var pos = ZoomAnchorMath.PositionForAnchor(cursor, ShadowMargin, positionUnitsPerDip, anchor, newDisplaySize);

        MarkZoomInteractive();
        CommitZoomGeometry(pos, newWindowSize, newDisplaySize);
    }

    /// <summary>
    /// 把算好的目标几何落盘。平台分流：
    /// mac 一次原子 <c>setFrame:display:</c>（几何与重绘本来就同帧，多拆一次反而撕裂）；
    /// Windows 拆三拍，见 <see cref="CommitZoom"/> 的说明。
    /// </summary>
    /// <param name="pos">目标窗口左上</param>
    /// <param name="windowSize">目标窗口尺寸（已对齐整物理像素）</param>
    /// <param name="displaySize">目标图片显示尺寸（用于托管兜底路径的记账）</param>
    private void CommitZoomGeometry(PixelPoint pos, Size windowSize, Size displaySize)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (this.TrySetWindowFrame(pos, windowSize, out Size applied))
            {
                _currentScale = ToDisplaySize(applied).Width / _originSize.Width;
                ApplyWindowSize(applied);
                return;
            }

            _currentScale = displaySize.Width / _originSize.Width;
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Position = pos;
                SetDisplaySize(displaySize);
            }, DispatcherPriority.MaxValue);
            return;
        }

        // ① 只落尺寸，位置留在原地
        if (!this.TrySetWindowFrame(Position, windowSize, out Size sized))
        {
            _currentScale = displaySize.Width / _originSize.Width;
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Position = pos;
                SetDisplaySize(displaySize);
            }, DispatcherPriority.MaxValue);
            return;
        }

        // 记账跟着尺寸走；Width/Height 也必须在渲染前就位，否则画出来的仍是旧尺寸
        _currentScale = ToDisplaySize(sized).Width / _originSize.Width;
        ApplyWindowSize(sized);

        // ② 把这一帧画完：布局 → 合成批 → 绘制 → 呈现
        FlushFrame();

        // ③ 纯移动：尺寸原样再传一遍（已对齐，幂等），内容已就位，搬窗口不留痕
        if (!this.TrySetWindowFrame(pos, sized, out Size moved)) Position = pos;
        ApplyWindowSize(moved);
    }

    /// <summary>
    /// 把刚提交的这一帧同步画完（布局 → 合成批 → 绘制 → 呈现），让窗口矩形与内容同帧落地。
    /// <para>
    /// 对照 macOS：那边 <c>setFrame:display:animate:</c> 传的是 <c>display:YES</c>，重绘跟着几何变更一起落地，
    /// 所以窗口矩形和内容永远同帧。Windows 默认渲染跑在独立线程上，DWM 已经按新矩形合成、内容还停在旧尺寸
    /// （实测落后 1~2 帧，就是缩放时「位置先跳过去又跳回来」那下抖）。
    /// </para>
    /// <para>
    /// 两步走，缺一不可：
    /// 1. <c>RunJobs(Render)</c> 跑掉 <c>MediaContext</c> 排在 Render 优先级的布局+合成批提交；
    /// 2. 强制一发 WM_PAINT，走 <c>CompositingRenderer.Paint → ImmediateRenderRequested</c> 的同步呈现
    ///    （等价 <c>display:YES</c>）。少了第 2 步，呈现要等下一次渲染循环节拍，错帧照样出现。
    /// </para>
    /// </summary>
    private void FlushFrame()
    {
        if (!OperatingSystem.IsWindows()) return;
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        if (!Win32Native.TryGetHwnd(this, out var hwnd)) return;
        Win32Native.InvalidateRect(hwnd, IntPtr.Zero, false);
        Win32Native.UpdateWindow(hwnd);
    }

    // 缩放上下限收敛到「显示尺寸」这一处：滚轮累积时也要夹，否则一次攒过多格会冲过边界再弹回
    private double ClampZoomScale(double scale)
    {
        if (_originSize.Width <= 0 || _originSize.Height <= 0) return 1;
        double minScale = Math.Min(MinDisplayLength / _originSize.Width, MinDisplayLength / _originSize.Height);
        double maxScale = Math.Min(_maxDisplaySize.Width / _originSize.Width,
            _maxDisplaySize.Height / _originSize.Height);
        if (double.IsFinite(maxScale)) scale = Math.Min(scale, maxScale);
        return Math.Max(scale, minScale);
    }

    // 手势期间锁住锚点比例：只在「隔了太久」或「光标真的动了」时从当前真实几何重新取样。
    // 每格重新取样会让取整误差随机游走（实测滚 12 格攒到 2px 以上，回滚时看着像抖）。
    private Point ResolveZoomAnchor(PixelPoint cursor, PixelPoint framePosition, double positionUnitsPerDip)
    {
        long now = Environment.TickCount64;
        bool resample = _currentSize.Width <= 0 || _currentSize.Height <= 0
                        || now - _zoomAnchorTickMs > ZoomAnchorIdleMs
                        || Math.Abs(cursor.X - _zoomAnchorCursor.X) > ZoomAnchorCursorSlack
                        || Math.Abs(cursor.Y - _zoomAnchorCursor.Y) > ZoomAnchorCursorSlack;
        if (resample)
        {
            _zoomAnchor = ZoomAnchorMath.NormalizeAnchor(cursor, framePosition, ShadowMargin, positionUnitsPerDip,
                _currentSize);
            _zoomAnchorCursor = cursor;
        }

        _zoomAnchorTickMs = now;
        return _zoomAnchor;
    }

    // 缩放的锚点必须取「当前真实光标」，不能取 e.GetPosition(ImageContent)：
    // Windows 上窗口被 SetWindowPos 搬走后系统不会补发 WM_MOUSEMOVE，
    // Avalonia 缓存的客户端内坐标就停在搬走前那一刻；拿它当锚点，每格都会偏一点，连滚就是肉眼可见的漂+抖。
    // mac 那条路是 setFrame:display:animate:，窗口移动和指针同步走，问题被掩盖了。
    // 纯 Wayland 拿不到全局光标时才退回客户端内坐标（此时窗口基本不会被搬太远）。
    private PixelPoint ResolveZoomCursor(double positionUnitsPerDip)
    {
        // 事件自带的客户端内坐标：永远有值，但窗口被搬走后会停在旧值。
        // 换算成 Position 单位后与全局光标可以直接比
        var fromEvent = new PixelPoint(
            Position.X + (int)Math.Round((_wheelImagePoint.X + ShadowMargin.Left) * positionUnitsPerDip),
            Position.Y + (int)Math.Round((_wheelImagePoint.Y + ShadowMargin.Top) * positionUnitsPerDip));

        if (!App.ScreensService.IsMousePositionReliable) return fromEvent;

        // 全局光标是真实值、不随窗口搬动失效，优先用它。
        // 但它在 Windows 上取自钩子缓存的最后一个事件坐标，钩子没起来时会一直是旧值（可能是 (0,0)），
        // 所以只在它落在窗口附近时才采信，否则退回事件坐标
        var global = App.ScreensService.MousePosition;
        double slack = 4 * positionUnitsPerDip;
        bool inside = global.X >= Position.X - slack && global.Y >= Position.Y - slack
                      && global.X <= Position.X + ClientSize.Width * positionUnitsPerDip + slack
                      && global.Y <= Position.Y + ClientSize.Height * positionUnitsPerDip + slack;
        return inside ? global : fromEvent;
    }

    // 手势期间切到 LowQuality、停稳后还原。
    // 注：实测 Avalonia 12 里 Unspecified 与 LowQuality 同为双线性（只有 HighQuality 是 cubic），
    // 所以这一步目前基本是空转；留着是为了 ImageContent 将来被主题设成 HighQuality 时能生效
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
            if (_editMode)
            {
                SaveEditMode(); // 编辑态双击 = 保存退出
                return;
            }

            SafeClose(0.1f);
            return;
        }

        if (_editMode) return; // 编辑器接管左键绘制，不再拖窗

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // mac 交给系统拖动循环（performWindowDragWithEvent:），本来就允许拖出屏幕；
        // Windows 的系统拖动循环（SC_MOVE）会把无边框窗当成「整窗都是标题栏」，卡在屏幕边挪不出去，
        // 所以 Windows 走自绘拖动：全局光标定位置 + 每帧一次原子提交，位置不再问窗口自己（见 CommitDrag）。
        // 停靠栏跟随也不能再问窗口在哪，改用按下时记下的抓取偏移按全局鼠标反推，见 DockAnchorPosition
        if (!App.ScreensService.IsMousePositionReliable) return;
        _dragGrabOffset = App.ScreensService.MousePosition - Position;
        double dragUnits = DisplayUnits.WindowPositionUnitsPerDip(this);
        // 抓取偏移存 DIP：跨屏 DPI 变化时按新屏的倍率换算，才不会突然错位一段
        _dragOffsetDip = dragUnits > 0
            ? new Point(_dragGrabOffset.X / dragUnits, _dragGrabOffset.Y / dragUnits)
            : new Point(0, 0);
        BeginDrag();
        if (OperatingSystem.IsWindows()) BeginManagedDrag();
        else BeginMoveDrag(e);
    }

    // 系统拖动期间窗口照常收得到移动事件（只有 Position 是滞后的），跟随就挂在这里。
    // 自绘拖动每帧自己在 CommitDrag 里触发跟随，这里不用重复推
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isDragging || _managedDrag) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        DockAnchorChanged?.Invoke();
    }

    // 自绘拖动：Windows 专用。系统拖动循环不允许把窗口拖出屏幕，只能自己按全局光标搬。
    // 只在 UI 线程上提交、且每帧最多一次：钩子线程只负责敲门，位置一律在提交那一刻现取
    private void BeginManagedDrag()
    {
        if (_managedDrag) return;
        _managedDrag = true;
        InputManager.Instance.EventOnMouseMoved += OnHookMoved;
    }

    private void EndManagedDrag()
    {
        if (!_managedDrag) return;
        _managedDrag = false;
        InputManager.Instance.EventOnMouseMoved -= OnHookMoved;
    }

    // 钩子线程回调，只转发；同帧多次移动合并成一次提交
    private void OnHookMoved(SharpHook.Data.MouseEventData data)
    {
        if (!_managedDrag || _dragCommitQueued) return;
        _dragCommitQueued = true;
        Dispatcher.UIThread.Post(CommitDrag, DispatcherPriority.Render);
    }

    private void CommitDrag()
    {
        _dragCommitQueued = false;
        if (!_managedDrag || !IsVisible || _currentSize.Width <= 0 || _currentSize.Height <= 0) return;

        // 位置 = 全局光标 - 抓取偏移（偏移按当前屏倍率换算，跨屏不出错位）
        double units = DisplayUnits.WindowPositionUnitsPerDip(this);
        var cursor = App.ScreensService.MousePosition;
        var pos = new PixelPoint(
            cursor.X - (int)Math.Round(_dragOffsetDip.X * units),
            cursor.Y - (int)Math.Round(_dragOffsetDip.Y * units));

        // 尺寸原样传：纯位移不触发重绘，DWM 直接搬表面，逐帧都跟手；
        // 而且没有系统拖动循环那套「必须留在屏幕内」的约束
        this.TrySetWindowFrame(pos, ToWindowSize(_currentSize));
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
        EndManagedDrag();
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
        ShowOcrTip(Loc.Text(LangKey.PreviewOcr_Recognizing), autoHide: false);
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
                ShowOcrTip(Loc.Text(LangKey.PreviewOcr_NoText), autoHide: true);
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
            .ShowNotification($"{Loc.Text(LangKey.PreviewOcr_Copied)}：{preview}", severity: MessageSeverity.Success);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        EndDrag();
        ResetOcr();
        if (_editMode) ExitEditModeCore();
        SafeSetImage(null);
    }

    public override void Hide()
    {
        base.Hide();
        EndDrag();
        ResetOcr();
        if (_editMode) ExitEditModeCore();
        SafeSetImage(null);
    }
}