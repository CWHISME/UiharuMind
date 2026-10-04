using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 设置页的通用设置行：标题 + 说明 + 控件（+ 可选的单项恢复默认）。
/// 视觉在 <c>SettingsRow.axaml</c>。用法：
/// <code>
/// &lt;controls:SettingsRow Header="{loc:Loc ThemeSetting}" Description="{loc:Loc ThemeSettingDesc}"&gt;
///     &lt;controls:SettingsRow.Field&gt;&lt;ComboBox Width="160" /&gt;&lt;/controls:SettingsRow.Field&gt;
/// &lt;/controls:SettingsRow&gt;
/// </code>
///
/// 口径：行 <c>MinHeight=44</c>，说明一律走全局 <c>muted</c>（12 + SemiGrey5）。
/// 内容走 <see cref="Field"/>：UserControl 的 <c>Content</c> 已被 axaml 根元素占用。
/// </summary>
public partial class SettingsRow : UserControl
{
    /// <summary>行标题</summary>
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Header));

    /// <summary>标题下的说明（12 + 全局 muted），可省</summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Description));

    /// <summary>行右侧的控件</summary>
    public static readonly StyledProperty<object?> FieldProperty =
        AvaloniaProperty.Register<SettingsRow, object?>(nameof(Field));

    /// <summary>是否显示行底分隔线。卡片里最后一行设 false</summary>
    public static readonly StyledProperty<bool> DividerVisibleProperty =
        AvaloniaProperty.Register<SettingsRow, bool>(nameof(DividerVisible), true);

    /// <summary>单项恢复默认。给了命令才出现「恢复默认」按钮</summary>
    public static readonly StyledProperty<ICommand?> ResetCommandProperty =
        AvaloniaProperty.Register<SettingsRow, ICommand?>(nameof(ResetCommand));

    /// <summary>
    /// 恢复默认按钮是否可见，默认 true。<b>不绑定命令时按钮本来就隐藏</b>；
    /// 这个属性是给「值≠出厂值」用的：值在默认上时把按钮藏起来，改了才冒出来。
    /// 默认 true 保兼容——没绑它的行照旧显示。
    /// </summary>
    public static readonly StyledProperty<bool> ResetVisibleProperty =
        AvaloniaProperty.Register<SettingsRow, bool>(nameof(ResetVisible), true);

    /// <summary>恢复默认按钮的提示文案，不给则用全局 ResetDefaults</summary>
    public static readonly StyledProperty<string?> ResetToolTipProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(ResetToolTip));

    public SettingsRow()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    /// <summary>行标题</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>标题下的说明，可省</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>行右侧的控件</summary>
    public object? Field
    {
        get => GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    /// <summary>是否显示行底分隔线，默认显示</summary>
    public bool DividerVisible
    {
        get => GetValue(DividerVisibleProperty);
        set => SetValue(DividerVisibleProperty, value);
    }

    /// <summary>单项恢复默认</summary>
    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    /// <summary>恢复默认按钮是否可见，默认 true</summary>
    public bool ResetVisible
    {
        get => GetValue(ResetVisibleProperty);
        set => SetValue(ResetVisibleProperty, value);
    }

    /// <summary>恢复默认按钮的提示文案</summary>
    public string? ResetToolTip
    {
        get => GetValue(ResetToolTipProperty);
        set => SetValue(ResetToolTipProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HeaderProperty || change.Property == DescriptionProperty)
        {
            UpdateVisibility();
        }
        else if ((change.Property == ResetCommandProperty || change.Property == ResetVisibleProperty)
                 && ResetButton is not null)
        {
            ResetButton.IsVisible = ResetCommand is not null && ResetVisible;
        }
    }

    private void UpdateVisibility()
    {
        if (HeaderText is not null)
        {
            HeaderText.IsVisible = !string.IsNullOrWhiteSpace(Header);
        }

        if (DescriptionText is not null)
        {
            DescriptionText.IsVisible = !string.IsNullOrWhiteSpace(Description);
        }
    }
}
