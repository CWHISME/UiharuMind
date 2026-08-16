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

        this.SetSimpledecorationWindow();
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

    private double _aspectRatio = 1.0f;
    private double _currentScale = 1.0f;
    private Size _currentSize;
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
        _originSize = size ?? image.PixelSize.ToSize(scaling);
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

        if (pos == null) this.SetWindowToMousePosition(horizontalAlignment, verticalAlignment, _originSize.Width, _originSize.Height);
    }

    protected override void OnInitWindowPosition()
    {
        // base.OnInitWindowPosition();
    }

    private void SetImageSize(Size newSize)
    {
        _currentSize = newSize;
        this.Width = newSize.Width;
        this.Height = newSize.Height;
        // ClientSize = newSize;
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
            // float sign = Math.Sign(e.Delta.Y);
            var newScale = (float)(_currentScale * (1 + e.Delta.Y * ScaleStep));

            // 限制缩放比例在最小和最大值之间
            // newScale < MinScale || newScale > MaxScale ||
            if (newScale > _currentScale &&
                (this._currentSize.Width >= MaxWidth || this._currentSize.Height >= MaxHeight) ||
                //限制最小缩放
                newScale < _currentScale &&
                (this._currentSize.Width <= MinWidth || this._currentSize.Height <= MinHeight))
            {
                return;
            }

            // if ((_currentSize.Width > Width || _currentSize.Height > Height) && newScale > _currentScale) return;
            // if ((_currentSize.Width < MinWidth || _currentSize.Height < MinHeight) && newScale < _currentScale) return;

            // 计算缩放前后鼠标位置的变化
            // var oldMousePos = new Point(mousePosition.X / _currentScale, mousePosition.Y / _currentScale);
            // var newMousePos = new Point(mousePosition.X / newScale, mousePosition.Y / newScale);

            // 更新当前缩放比例
            _currentScale = newScale;

            // Dispatcher.UIThread.Post(() =>
            // {
            // 调整窗口大小以适应新的内容大小
            // var newWidth = Math.Clamp(_originSize.Width * _currentScale, 0, MaxWidth);
            // var newHeight = Math.Clamp(_originSize.Height * _currentScale, 0, MaxHeight);
            // 计算新的宽度，并限制在上下限之间
            // var newWidth = Math.Clamp(_originSize.Width * _currentScale, 0, MaxWidth);

            var newSize =
                _originSize.ScaleByWidth(_currentScale, _aspectRatio, MinWidth, MinHeight, MaxWidth, MaxHeight);
            // // 计算图像宽度和高度的变化量
            // var widthChange = newWidth - _currentSize.Width;
            // var heightChange = newHeight - _currentSize.Height;
            // // if (sign > 0)
            // {
            //     widthChange *= 0.5f;
            //     heightChange *= 0.5f;
            // }

            // 计算新的窗口位置
            double zoomX = newSize.Width / _currentSize.Width;
            double zoomY = newSize.Height / _currentSize.Height;

            //调整窗口位置
            int newPosX = (int)(curPos.X - (mousePosition.X * (zoomX - 1)));
            int newPosY = (int)(curPos.Y - (mousePosition.Y * (zoomY - 1)));

            var pos = new PixelPoint(newPosX, newPosY);
            // var size = new Size((int)newWidth, (int)newHeight);

            //确保鼠标位置在缩放后不超出界面
            pos += UiUtils.EnsureMousePositionWithinTargetOffset(pos, newSize);

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // StopRendering();
                this.Position = pos;
                SetImageSize(newSize);
                // StartRendering();
                // InvalidateMeasure();
            }, DispatcherPriority.MaxValue);
            // });

            e.Handled = true;
        }
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
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            var position = e.GetPosition(this);
            var diff = position - _dragStartPoint;

            var windowPosition = this.Position;
            windowPosition = new PixelPoint(
                (int)Math.Round(windowPosition.X + diff.X),
                (int)Math.Round(windowPosition.Y + diff.Y)
            );
            // Log.Debug($"windowPosition: {windowPosition}");
            this.Position = windowPosition;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
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
