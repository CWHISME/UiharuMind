using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 徽章：浅色圆角胶囊 + 文本。底色默认按 <see cref="IsAgent"/> 二态自动选（角色类别场景），
/// 也可用 <see cref="BadgeBackground"/> 显式覆盖（模型状态标签场景）。视觉在 <c>KindBadge.axaml</c>，
/// 角色列表、编辑页、建群、模型状态标签共用同一套，不各写各的。
///
/// <b>别碰继承的 <see cref="Background"/></b>：KindBadge 是 UserControl，Background 会被默认模板的
/// ContentPresenter 当成<b>整个控件</b>的背景铺出来（无圆角、撑满容器），徽章反而变直角大色块。
/// 背景只走 <see cref="BadgeBackground"/> 或 <see cref="IsAgent"/> 自动配色。
/// </summary>
public partial class KindBadge : UserControl
{
    /// <summary>显示文本</summary>
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<KindBadge, string>(nameof(Header));

    /// <summary>是不是智能体（<see cref="BadgeBackground"/> 未给时按它选底色：普通角色浅绿，智能体紫罗兰）</summary>
    public static readonly StyledProperty<bool> IsAgentProperty =
        AvaloniaProperty.Register<KindBadge, bool>(nameof(IsAgent));

    /// <summary>显式背景色；给定时优先于 <see cref="IsAgent"/> 的自动配色。不叫 Background 以免被 UserControl 当自身背景渲染</summary>
    public static readonly StyledProperty<IBrush?> BadgeBackgroundProperty =
        AvaloniaProperty.Register<KindBadge, IBrush?>(nameof(BadgeBackground));

    /// <summary>最终底色：显式给的优先，否则按类别自动选（两把刷子收在 axaml 资源里）</summary>
    public static readonly StyledProperty<IBrush?> ResolvedBackgroundProperty =
        AvaloniaProperty.Register<KindBadge, IBrush?>(nameof(ResolvedBackground));

    static KindBadge()
    {
        BadgeBackgroundProperty.Changed.AddClassHandler<KindBadge>(static (badge, _) => badge.UpdateResolvedBackground());
        IsAgentProperty.Changed.AddClassHandler<KindBadge>(static (badge, _) => badge.UpdateResolvedBackground());
    }

    public KindBadge()
    {
        InitializeComponent();
        UpdateResolvedBackground();
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
    /// 是不是智能体
    /// </summary>
    public bool IsAgent
    {
        get => GetValue(IsAgentProperty);
        set => SetValue(IsAgentProperty, value);
    }

    /// <summary>
    /// 显式背景色
    /// </summary>
    public IBrush? BadgeBackground
    {
        get => GetValue(BadgeBackgroundProperty);
        set => SetValue(BadgeBackgroundProperty, value);
    }

    /// <summary>
    /// 最终底色
    /// </summary>
    public IBrush? ResolvedBackground
    {
        get => GetValue(ResolvedBackgroundProperty);
        private set => SetValue(ResolvedBackgroundProperty, value);
    }

    private IBrush? ChatBackgroundBrush => Resources["KindBadgeChatBackground"] as IBrush;

    private IBrush? AgentBackgroundBrush => Resources["KindBadgeAgentBackground"] as IBrush;

    private void UpdateResolvedBackground() =>
        ResolvedBackground = BadgeBackground ?? (IsAgent ? AgentBackgroundBrush : ChatBackgroundBrush);
}
