using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using UiharuMind.Services;

namespace UiharuMind.Controls
{
    public class RadialMenuItem : TemplatedControl
    {
        public static readonly StyledProperty<string> IconProperty =
            AvaloniaProperty.Register<RadialMenuItem, string>(nameof(Icon));
        public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<RadialMenuItem, string>(nameof(Text));
        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

        public static readonly StyledProperty<ICommand> CommandProperty =
            AvaloniaProperty.Register<RadialMenuItem, ICommand>(nameof(Command));
        public ICommand Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

        public static readonly StyledProperty<Geometry> SliceGeometryProperty =
            AvaloniaProperty.Register<RadialMenuItem, Geometry>(nameof(SliceGeometry));
        public Geometry SliceGeometry { get => GetValue(SliceGeometryProperty); set => SetValue(SliceGeometryProperty, value); }

        public static readonly StyledProperty<Point> IconPositionProperty =
            AvaloniaProperty.Register<RadialMenuItem, Point>(nameof(IconPosition));
        public Point IconPosition { get => GetValue(IconPositionProperty); set => SetValue(IconPositionProperty, value); }

        public static readonly StyledProperty<IBrush> SliceFillProperty =
            AvaloniaProperty.Register<RadialMenuItem, IBrush>(nameof(SliceFill));
        public IBrush SliceFill { get => GetValue(SliceFillProperty); set => SetValue(SliceFillProperty, value); }

        public bool IsHovered { get; private set; }

        private Color _normalColor;
        private Color _hoverColor;

        private CancellationTokenSource? _animationCts;

        public RadialMenuItem()
        {
            UpdateThemeColors();
            SliceFill = new SolidColorBrush(_normalColor);
            RenderTransform = new ScaleTransform(1, 1);
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (Application.Current != null)
                Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (Application.Current != null)
                Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            UpdateThemeColors();
            SliceFill = new SolidColorBrush(IsHovered ? _hoverColor : _normalColor);
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            IsHovered = true;
            SliceFill = new SolidColorBrush(_hoverColor);
            AnimateScaleTo(1.05);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            IsHovered = false;
            SliceFill = new SolidColorBrush(_normalColor);
            AnimateScaleTo(1.0);
        }

        private void UpdateThemeColors()
        {
            bool isDark = ApplicationThemeManager.IsDarkTheme();
            _normalColor = isDark ? Color.Parse("#E02D2D3D") : Color.Parse("#E0D0D0D0");
            _hoverColor = isDark ? Color.Parse("#CC66BBFF") : Color.Parse("#CC3399FF");
        }

        private async void AnimateScaleTo(double targetScale)
        {
            _animationCts?.Cancel();
            _animationCts = new CancellationTokenSource();
            var ct = _animationCts.Token;

            if (RenderTransform is not ScaleTransform scale) return;

            double startScale = scale.ScaleX;
            var startTime = DateTime.UtcNow;
            const double durationMs = 200;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    if (elapsed >= durationMs) break;

                    double t = elapsed / durationMs;
                    double eased = t * t * (3 - 2 * t);
                    double current = startScale + (targetScale - startScale) * eased;

                    scale.ScaleX = current;
                    scale.ScaleY = current;

                    await Task.Delay(16, ct);
                }

                scale.ScaleX = targetScale;
                scale.ScaleY = targetScale;
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Execute()
        {
            if (Command.CanExecute(null))
            {
                Command.Execute(null);
            }
        }
    }
}
