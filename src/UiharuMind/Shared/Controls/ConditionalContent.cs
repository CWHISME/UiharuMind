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

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 条件为真时才以数据上下文套用 ContentTemplate 生成内容，为假时把内容整棵丢掉。
///
/// 给「绝大多数行都不显示」的部件用（列表行里的转圈、待审批点、批量勾选框、群徽章）。
/// 只绑 <c>IsVisible</c> 的话控件照样逐行构建、套样式，而虚拟化列表每回收一行就重建一次模板——
/// 实测左栏会话列表一次换整屏，这几样隐藏部件占了约三分之二的耗时。
/// 与 <see cref="HoverDeferredContent"/> 是兄弟：那个按悬停触发，这个按条件。
/// </summary>
public class ConditionalContent : ContentControl
{
    /// <summary>为真时生成内容、为假时丢掉的那个条件</summary>
    public static readonly StyledProperty<bool> WhenProperty =
        AvaloniaProperty.Register<ConditionalContent, bool>(nameof(When));

    /// <summary>
    /// 构造。初始不可见、不裁剪
    /// </summary>
    public ConditionalContent()
    {
        IsVisible = false; //When 默认为假,没收到属性变更前也不能空占一格
        // 它只是个包装层,表现得该和不存在一样:内容用负外边距对齐时(如标题行里的徽章)伸出去的部分不能被它裁掉
        ClipToBounds = false;
    }

    /// <summary>为真时生成内容，为假时丢掉</summary>
    public bool When
    {
        get => GetValue(WhenProperty);
        set => SetValue(WhenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WhenProperty && change.Property != DataContextProperty) return;

        IsVisible = When;
        Content = When ? DataContext : null;
    }
}
