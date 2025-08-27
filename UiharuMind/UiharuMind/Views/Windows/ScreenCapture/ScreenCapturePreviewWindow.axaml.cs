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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.SemanticKernel;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Utils;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.ScreenCapture;

public partial class ScreenCapturePreviewWindow : UiharuWindowBase, IDockedWindow //Window, IDockedWindow
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private Size _originSize;
    // private double _minScale;


    public Bitmap? ImageBackupSource;
    public Bitmap? ImageOriginSource;
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

    public void SetImage(Bitmap image, Size? size = null)
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

        this.SetWindowToMousePosition();
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
        var imageBackupSource = ImageBackupSource;
        var imageSource = ImageSource;
        var imageOriginSource = ImageOriginSource;

        ImageBackupSource = null;
        ImageSource = image;
        ImageOriginSource = image;
        ImageContent.Source = image;

        imageBackupSource?.Dispose();
        imageSource?.Dispose();
        imageOriginSource?.Dispose();
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
            // // Task.Run(() =>
            // // {
            // //     Task.Delay(1000);
            // //     SafeClose();
            // // });
            Close();
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

    protected override void OnPreClose()
    {
        base.OnPreClose();
        SafeSetImage(null);
    }
}