using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 圆形头像：图片按 UniformToFill 铺满圆，带一圈随主题的细描边。角色 / 会话 / 群成员的头像统一走它，
/// 尺寸与边距照常写在 Width / Height / Margin 上
/// </summary>
public class CircleAvatar : Ellipse
{
    /// <summary>头像图片</summary>
    public static readonly StyledProperty<IImageBrushSource?> SourceProperty =
        AvaloniaProperty.Register<CircleAvatar, IImageBrushSource?>(nameof(Source));

    static CircleAvatar()
    {
        SourceProperty.Changed.AddClassHandler<CircleAvatar>((avatar, _) => avatar.UpdateFill());
    }

    /// <summary>
    /// 构造：默认描边与高质量缩放（头像多是大图缩到几十像素，默认插值会起锯齿）
    /// </summary>
    public CircleAvatar()
    {
        StrokeThickness = 1;
        Bind(StrokeProperty, this.GetResourceObservable("SemiGrey2"));
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>头像图片</summary>
    public IImageBrushSource? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void UpdateFill() =>
        Fill = Source == null ? null : new ImageBrush(Source) { Stretch = Stretch.UniformToFill };
}
