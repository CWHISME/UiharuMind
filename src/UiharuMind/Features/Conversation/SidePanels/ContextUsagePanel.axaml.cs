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
/// 两处复用：输入框那个环点开的面板（完整，含水位说明）与右侧栏（只读摘要）。
/// 数据那一半在 <see cref="ContextUsageViewData"/>，进度条样式与行布局都在本文件。
/// </summary>
public partial class ContextUsagePanel : UserControl
{
    /// <summary>
    /// 是否显示补充信息（水位说明、思考统计等）。
    /// 右侧栏常驻时关掉，完整内容只在输入框那个环弹出的面板里给
    /// </summary>
    public static readonly StyledProperty<bool> ShowDetailsProperty =
        AvaloniaProperty.Register<ContextUsagePanel, bool>(nameof(ShowDetails), defaultValue: true);

    /// <summary>
    /// 是否显示补充信息（水位说明、思考统计等）
    /// </summary>
    public bool ShowDetails
    {
        get => GetValue(ShowDetailsProperty);
        set => SetValue(ShowDetailsProperty, value);
    }

    public ContextUsagePanel()
    {
        InitializeComponent();
    }
}
