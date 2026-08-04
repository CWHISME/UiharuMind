/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 对话类页面壳的公共部分：左右面板的宽度、开合与窄宽响应式收起。
/// </summary>
public abstract partial class ConversationPageDataBase : PageDataBase
{
    /// <summary>低于此宽度收起右栏</summary>
    private const double RightPaneCollapseWidth = 888;

    /// <summary>低于此宽度同时收起左右栏</summary>
    private const double BothPanesCollapseWidth = 666;

    [ObservableProperty] private float _leftPaneWidth = 200;
    [ObservableProperty] private float _rightPaneWidth = 200;
    [ObservableProperty] private bool _isLeftPaneOpen = true;
    [ObservableProperty] private bool _isRightPaneOpen = true;

    /// <summary>
    /// 窗口宽度响应：过窄时自动收起面板
    /// </summary>
    /// <param name="width">当前宽度</param>
    public void UpdateResponsiveState(double width)
    {
        if (width <= 0) return;
        if (width < BothPanesCollapseWidth)
        {
            IsLeftPaneOpen = false;
            IsRightPaneOpen = false;
        }
        else if (width < RightPaneCollapseWidth)
        {
            IsRightPaneOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleLeftPane()
    {
        IsLeftPaneOpen = !IsLeftPaneOpen;
    }

    [RelayCommand]
    private void ToggleRightPane()
    {
        IsRightPaneOpen = !IsRightPaneOpen;
    }
}
