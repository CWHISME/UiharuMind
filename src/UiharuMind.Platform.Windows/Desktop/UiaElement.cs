/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Interop.UIAutomationClient;

namespace UiharuMind.Platform.Windows.Desktop;

/// <summary>
/// 快照节点背后的 UIA 元素，连同读树时顺带查好的「支持哪些操作」，动手时不必再跨进程问一遍
/// </summary>
/// <param name="Element">元素</param>
/// <param name="ControlType">控件类型（UIA_ControlTypeIds）</param>
internal sealed record UiaElement(IUIAutomationElement Element, int ControlType)
{
    /// <summary>能「调用」（按钮、链接、菜单项）</summary>
    public bool CanInvoke { get; init; }

    /// <summary>能切换（复选框）</summary>
    public bool CanToggle { get; init; }

    /// <summary>能被选中（列表项、标签页、树节点）</summary>
    public bool CanSelect { get; init; }

    /// <summary>能展开收起（下拉框、树节点、菜单）</summary>
    public bool CanExpand { get; init; }

    /// <summary>值能写（输入框、可编辑的下拉框）</summary>
    public bool CanSetValue { get; init; }

    /// <summary>有 MSAA 的默认动作（老控件的兜底）</summary>
    public bool HasDefaultAction { get; init; }
}
