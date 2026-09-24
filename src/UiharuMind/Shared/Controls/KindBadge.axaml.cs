using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 徽章：浅色圆角胶囊 + 文本。纯展示组件，不含业务语义：
/// 底色由 <see cref="BadgeBackground"/> 指定，调用方负责配（角色类别在 CharacterKindPresentation，
/// 模型状态标签直接给字面色）。视觉在 <c>KindBadge.axaml</c>。
///
/// <b>别碰继承的 <see cref="Background"/></b>：KindBadge 是 UserControl，Background 会被默认模板的
/// ContentPresenter 当成<b>整个控件</b>的背景铺出来（无圆角、撑满容器），徽章反而变直角大色块。
/// 背景只走 <see cref="BadgeBackground"/>。
/// </summary>
public partial class KindBadge : UserControl
{
    /// <summary>显示文本</summary>
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<KindBadge, string>(nameof(Header));

    /// <summary>底色。不叫 Background 以免被 UserControl 当自身背景渲染；不给出时徽章透明、只剩文字</summary>
    public static readonly StyledProperty<IBrush?> BadgeBackgroundProperty =
        AvaloniaProperty.Register<KindBadge, IBrush?>(nameof(BadgeBackground));

    public KindBadge()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 显示文本
    /// </summary>
    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// 底色
    /// </summary>
    public IBrush? BadgeBackground
    {
        get => GetValue(BadgeBackgroundProperty);
        set => SetValue(BadgeBackgroundProperty, value);
    }
}