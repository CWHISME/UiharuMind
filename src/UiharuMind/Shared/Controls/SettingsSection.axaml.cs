using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 设置页的分节卡片：分节标题 + 卡片容器。视觉在 <c>SettingsSection.axaml</c>。
///
/// 用法：
/// <code>
/// &lt;controls:SettingsSection Header="{loc:Loc ApplicationUpdate}"&gt;
///     &lt;controls:SettingsSection.Body&gt;
///         &lt;StackPanel&gt; ... &lt;controls:SettingsRow ... /&gt; ... &lt;/StackPanel&gt;
///     &lt;/controls:SettingsSection.Body&gt;
/// &lt;/controls:SettingsSection&gt;
/// </code>
/// 内容刻意走 <see cref="Body"/> 而不是继承的 <c>Content</c>：UserControl 的 <c>Content</c>
/// 已被 axaml 根元素占用（KindBadge 那条注释踩过同一个坑），另立名字才不留歧义。
/// </summary>
public partial class SettingsSection : UserControl
{
    /// <summary>分节标题</summary>
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SettingsSection, string?>(nameof(Header));

    /// <summary>标题下的补充说明（12 + 全局 muted），可省</summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsSection, string?>(nameof(Description));

    /// <summary>分节标题字号。页面级分节用默认 15；页面内嵌的小卡片传 13</summary>
    public static readonly StyledProperty<double> HeaderFontSizeProperty =
        AvaloniaProperty.Register<SettingsSection, double>(nameof(HeaderFontSize), 15d);

    /// <summary>卡片内容</summary>
    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<SettingsSection, object?>(nameof(Body));

    public SettingsSection()
    {
        InitializeComponent();
        UpdateChromeVisibility();
    }

    /// <summary>分节标题</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>标题下的补充说明，可省</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>分节标题字号，默认 15</summary>
    public double HeaderFontSize
    {
        get => GetValue(HeaderFontSizeProperty);
        set => SetValue(HeaderFontSizeProperty, value);
    }

    /// <summary>卡片内容</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HeaderProperty
            || change.Property == DescriptionProperty
            || change.Property == BodyProperty)
        {
            UpdateChromeVisibility();
        }
    }

    /// <summary>
    /// 空标题/空说明/空 Body 都不能占位：TextBlock 空串照样撑起一行行高，
    /// 空 Body 则整张卡片都是多余的描边框。
    /// </summary>
    private void UpdateChromeVisibility()
    {
        bool hasHeader = !string.IsNullOrWhiteSpace(Header);
        bool hasDescription = !string.IsNullOrWhiteSpace(Description);

        if (HeaderText is not null)
        {
            HeaderText.IsVisible = hasHeader;
        }

        if (DescriptionText is not null)
        {
            DescriptionText.IsVisible = hasDescription;
        }

        // 标题区整体：都没值时连容器一起收起，不然外层 Spacing=8 留一条幽灵缝
        if (HeaderBlock is not null)
        {
            HeaderBlock.IsVisible = hasHeader || hasDescription;
        }

        if (CardBorder is not null)
        {
            CardBorder.IsVisible = Body is not null;
        }
    }
}
