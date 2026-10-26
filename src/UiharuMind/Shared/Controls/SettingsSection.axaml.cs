using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

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

    /// <summary>
    /// 标题与说明是否收进卡片内部（「整卡」样式）。默认 false = 标题区在卡片外
    /// （页面级分节）；Agent 常规页那几块全局能力传 true，避免标题孤零零露在卡片外。
    /// </summary>
    public static readonly StyledProperty<bool> HeaderInsideCardProperty =
        AvaloniaProperty.Register<SettingsSection, bool>(nameof(HeaderInsideCard), false);

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

    /// <summary>标题与说明是否收进卡片内部（整卡样式），默认 false</summary>
    public bool HeaderInsideCard
    {
        get => GetValue(HeaderInsideCardProperty);
        set => SetValue(HeaderInsideCardProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HeaderProperty
            || change.Property == DescriptionProperty
            || change.Property == BodyProperty
            || change.Property == HeaderInsideCardProperty)
        {
            // 编译绑定后属性变更可能来自后台线程（如测试清理里 ReplaceModels 的回调）：
            // UI 属性跨线程访问会抛 VerifyAccess。Headless 并行下 CheckAccess 不可靠，
            // 统一 Post 到 UI 线程刷新可见性（IsVisible 不触发本回调，不会循环）
            Dispatcher.UIThread.Post(UpdateChromeVisibility);
        }
    }

    /// <summary>
    /// 空标题/空说明/空 Body 都不能占位：TextBlock 空串照样撑起一行行高，
    /// 空 Body 则整张卡片都是多余的描边框。
    /// 内置模式（HeaderInsideCard）下标题区进卡片：卡片显隐条件要含标题，否则纯标题整卡会整个消失。
    /// </summary>
    private void UpdateChromeVisibility()
    {
        bool hasHeader = !string.IsNullOrWhiteSpace(Header);
        bool hasDescription = !string.IsNullOrWhiteSpace(Description);
        bool inside = HeaderInsideCard;

        // 外置标题区：默认模式才显示，内置模式整组收起
        if (HeaderBlock is not null)
        {
            HeaderBlock.IsVisible = !inside && (hasHeader || hasDescription);
        }

        if (HeaderText is not null)
        {
            HeaderText.IsVisible = !inside && hasHeader;
        }

        if (DescriptionText is not null)
        {
            DescriptionText.IsVisible = !inside && hasDescription;
        }

        // 内置标题区：HeaderInsideCard 模式才显示
        if (HeaderInsideBlock is not null)
        {
            HeaderInsideBlock.IsVisible = inside && (hasHeader || hasDescription);
        }

        if (HeaderInsideText is not null)
        {
            HeaderInsideText.IsVisible = inside && hasHeader;
        }

        if (DescriptionInsideText is not null)
        {
            DescriptionInsideText.IsVisible = inside && hasDescription;
        }

        if (CardBorder is not null)
        {
            CardBorder.IsVisible = Body is not null || (inside && (hasHeader || hasDescription));
        }
    }
}
