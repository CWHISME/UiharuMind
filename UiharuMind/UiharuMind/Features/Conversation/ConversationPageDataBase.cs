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
/// 此前 ChatPageData 与 AgentPageData 各有一份逐行相同的实现
/// （AgentPageData 的注释"对齐 ChatPageData"就是这份重复的自述），
/// 且已经漂移出差异（右栏默认宽度一个 320 一个 300）。
/// </summary>
public abstract partial class ConversationPageDataBase : PageDataBase
{
    /// <summary>低于此宽度收起右栏</summary>
    private const double RightPaneCollapseWidth = 1040;

    /// <summary>低于此宽度同时收起左右栏</summary>
    private const double BothPanesCollapseWidth = 860;

    [ObservableProperty] private float _leftPaneWidth = 260;
    [ObservableProperty] private float _rightPaneWidth = 320;
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
