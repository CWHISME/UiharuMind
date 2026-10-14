/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 上下文占用的统计面板。DataContext 即 <see cref="ContextUsageViewData"/>。
///
/// 输入框 token 统计的 ToolTip 与右侧栏复用的同一份控件；
/// 数据那一半在 <see cref="ContextUsageViewData"/>，进度条样式与行布局都在本文件。
/// </summary>
public partial class ContextUsagePanel : UserControl
{
    /// <summary>
    /// 是否显示水位说明文字（交接/折叠/截断）。
    /// 右侧栏常驻时关掉，描述只在输入框 ToolTip 里弹出
    /// </summary>
    public static readonly StyledProperty<bool> ShowDescriptionsProperty =
        AvaloniaProperty.Register<ContextUsagePanel, bool>(nameof(ShowDescriptions), defaultValue: true);

    /// <summary>
    /// 是否显示水位说明文字（交接/折叠/截断）
    /// </summary>
    public bool ShowDescriptions
    {
        get => GetValue(ShowDescriptionsProperty);
        set => SetValue(ShowDescriptionsProperty, value);
    }

    public ContextUsagePanel()
    {
        InitializeComponent();
    }
}
