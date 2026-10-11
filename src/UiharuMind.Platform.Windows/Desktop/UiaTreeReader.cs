/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Interop.UIAutomationClient;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using static Interop.UIAutomationClient.UIA_ControlTypeIds;
using static Interop.UIAutomationClient.UIA_PropertyIds;

namespace UiharuMind.Platform.Windows.Desktop;

/// <summary>
/// 把一个窗口的 UI Automation 树读成 <see cref="AxNode"/>：按控件视图走，每个元素连同子元素一批读齐属性（缓存请求），
/// 一个有子元素的节点一次跨进程往返；控件类型映射成与网页同一套角色名，快照器、对账器、按字查找都照用
/// </summary>
internal sealed class UiaTreeReader
{
    private const int MaxDepth = 40;
    private const int MaxChildren = 300; //一个列表、表格最多读这么多项，余下的列成一行
    private const int HeadingLevelNone = 80050; //HeadingLevel 属性的「不是标题」，往上依次是一到九级
    private const int ToggleOn = 1;
    private const int ToggleIndeterminate = 2;
    private const int ExpandCollapsed = 0;
    private const int ExpandLeaf = 3;

    private static readonly int[] Properties =
    [
        UIA_NamePropertyId, UIA_ControlTypePropertyId, UIA_BoundingRectanglePropertyId, UIA_IsEnabledPropertyId,
        UIA_HasKeyboardFocusPropertyId, UIA_IsPasswordPropertyId, UIA_HelpTextPropertyId, UIA_ValueValuePropertyId,
        UIA_ValueIsReadOnlyPropertyId, UIA_ToggleToggleStatePropertyId, UIA_ExpandCollapseExpandCollapseStatePropertyId,
        UIA_SelectionItemIsSelectedPropertyId, UIA_HeadingLevelPropertyId, UIA_IsInvokePatternAvailablePropertyId,
        UIA_IsTogglePatternAvailablePropertyId, UIA_IsSelectionItemPatternAvailablePropertyId,
        UIA_IsExpandCollapsePatternAvailablePropertyId, UIA_IsValuePatternAvailablePropertyId,
        UIA_IsLegacyIAccessiblePatternAvailablePropertyId,
    ];

    // 滚动条、分隔条、提示、标题栏（最小化、关闭这几个按钮）不用读：没有内容，关窗用 Alt+F4
    private static readonly HashSet<int> SkippedTypes =
    [
        UIA_ScrollBarControlTypeId, UIA_ThumbControlTypeId, UIA_SeparatorControlTypeId, UIA_ToolTipControlTypeId,
        UIA_TitleBarControlTypeId,
    ];

    // 不往下读的：名字已说清里面是什么，往下只是拼出名字的碎字
    private static readonly HashSet<int> LeafTypes =
    [
        UIA_TextControlTypeId, UIA_ImageControlTypeId, UIA_EditControlTypeId, UIA_ButtonControlTypeId,
        UIA_HyperlinkControlTypeId, UIA_CheckBoxControlTypeId, UIA_RadioButtonControlTypeId,
        UIA_ProgressBarControlTypeId, UIA_SliderControlTypeId,
    ];

    // 只有这几类的展开状态有意义
    private static readonly HashSet<string> Expandable = ["combobox", "treeitem"];

    private readonly IUIAutomationCacheRequest _request;
    private int _budget;

    private UiaTreeReader(IUIAutomationCacheRequest request, int maxNodes)
    {
        _request = request;
        _budget = maxNodes;
    }

    /// <summary>
    /// 读一个窗口
    /// </summary>
    /// <param name="automation">UIA 客户端</param>
    /// <param name="hwnd">窗口</param>
    /// <param name="title">窗口标题（作根的名字）</param>
    /// <param name="maxNodes">最多读多少个节点</param>
    /// <returns>树，根是窗口</returns>
    public static AxNode Read(IUIAutomation automation, IntPtr hwnd, string title, int maxNodes)
    {
        IUIAutomationCacheRequest request = automation.CreateCacheRequest();
        foreach (int property in Properties) request.AddProperty(property);
        request.TreeScope = TreeScope.TreeScope_Element | TreeScope.TreeScope_Children;
        request.TreeFilter = automation.ControlViewCondition;

        IUIAutomationElement root = automation.ElementFromHandleBuildCache(hwnd, request);
        UiaTreeReader reader = new(request, maxNodes);
        return new AxNode("window", title)
        {
            Children = reader.ReadChildren(root, UIA_WindowControlTypeId, 1),
            Source = Wrap(root, UIA_WindowControlTypeId),
        };
    }

    private List<AxNode> ReadChildren(IUIAutomationElement parent, int parentType, int depth)
    {
        List<AxNode> children = new();
        if (depth > MaxDepth) return children;
        IUIAutomationElementArray? items = parent.GetCachedChildren();
        if (items == null) return children;

        int count = items.Length;
        int shown = Math.Min(count, MaxChildren);
        for (int i = 0; i < shown; i++)
        {
            if (_budget <= 0)
            {
                children.Add(More(count - i));
                return children;
            }

            if (ReadNode(items.GetElement(i), parentType, depth) is { } child) children.Add(child);
        }

        if (count > shown) children.Add(More(count - shown));
        return children;
    }

    private AxNode? ReadNode(IUIAutomationElement element, int parentType, int depth)
    {
        int type = element.CachedControlType;
        if (SkippedTypes.Contains(type)) return null;
        _budget--;

        AxNode node = Describe(element, type, parentType);
        // 没名字的图（图标、装饰）只占行
        if (node.Role == "img" && node.Name.Trim().Length == 0) return null;
        if (LeafTypes.Contains(type)) return node;

        // 再问一次它的子元素（连同属性）
        IUIAutomationElement expanded;
        try
        {
            expanded = element.BuildUpdatedCache(_request);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return node; //读的时候它没了（菜单收起、列表刷新）
        }

        return node with { Children = AxRoles.WithoutEchoes(node, ReadChildren(expanded, type, depth + 1)) };
    }

    private static AxNode Describe(IUIAutomationElement element, int type, int parentType)
    {
        bool writable = Flag(element, UIA_IsValuePatternAvailablePropertyId) && !Flag(element, UIA_ValueIsReadOnlyPropertyId);
        bool selectable = Flag(element, UIA_IsSelectionItemPatternAvailablePropertyId);
        int headingLevel = Number(element, UIA_HeadingLevelPropertyId) is { } level and > HeadingLevelNone ? level - HeadingLevelNone : 0;
        string role = MapRole(type, parentType, writable, selectable, headingLevel);

        string name = Text(element, UIA_NamePropertyId);
        string value = Text(element, UIA_ValueValuePropertyId);
        bool toggle = role is "checkbox" or "radio";
        bool link = role == "link";
        int? expand = Number(element, UIA_ExpandCollapseExpandCollapseStatePropertyId);
        return new AxNode(role, name)
        {
            Value = role is "text" or "heading" or "tab" || toggle || link ? string.Empty : value,
            Url = link ? value : string.Empty,
            Level = headingLevel,
            Checked = role == "checkbox" ? CheckedState(Number(element, UIA_ToggleToggleStatePropertyId))
                : role == "radio" ? (Flag(element, UIA_SelectionItemIsSelectedPropertyId) ? "true" : "false")
                : string.Empty,
            Selected = !toggle && Flag(element, UIA_SelectionItemIsSelectedPropertyId),
            Disabled = element.GetCachedPropertyValue(UIA_IsEnabledPropertyId) is false,
            Focused = Flag(element, UIA_HasKeyboardFocusPropertyId),
            Expanded = Expandable.Contains(role) && expand is { } state and not ExpandLeaf ? state != ExpandCollapsed : null,
            Secret = Flag(element, UIA_IsPasswordPropertyId),
            Description = Text(element, UIA_HelpTextPropertyId),
            Bounds = element.GetCachedPropertyValue(UIA_BoundingRectanglePropertyId) is double[] { Length: 4 } box && box[2] > 0 && box[3] > 0
                ? new AxRect(box[0], box[1], box[2], box[3])
                : null,
            Source = Wrap(element, type) with { CanSetValue = writable },
        };
    }

    // 与网页同一套角色名：AxRoles 据此决定编不编号、成不成行
    private static string MapRole(int type, int parentType, bool writable, bool selectable, int headingLevel) => type switch
    {
        UIA_ButtonControlTypeId or UIA_SplitButtonControlTypeId => "button",
        UIA_HeaderItemControlTypeId => "button", //列头，点了排序
        UIA_CheckBoxControlTypeId => "checkbox",
        UIA_RadioButtonControlTypeId => "radio",
        UIA_ComboBoxControlTypeId => "combobox",
        UIA_EditControlTypeId => "textbox",
        UIA_DocumentControlTypeId => writable ? "textbox" : "WebArea", //记事本的正文能写；网页只是个容器
        UIA_HyperlinkControlTypeId => "link",
        UIA_ImageControlTypeId => "img",
        UIA_ListControlTypeId => "list",
        UIA_ListItemControlTypeId or UIA_DataItemControlTypeId => selectable || parentType == UIA_ListControlTypeId ? "option" : "listitem",
        UIA_DataGridControlTypeId or UIA_TableControlTypeId => "table",
        UIA_TreeControlTypeId => "tree",
        UIA_TreeItemControlTypeId => "treeitem",
        UIA_MenuBarControlTypeId => "menubar",
        UIA_MenuControlTypeId => "menu",
        UIA_MenuItemControlTypeId => "menuitem",
        UIA_TabControlTypeId => "tablist",
        UIA_TabItemControlTypeId => "tab",
        UIA_SliderControlTypeId => "slider",
        UIA_SpinnerControlTypeId => "spinbutton",
        UIA_ProgressBarControlTypeId => "progressbar",
        UIA_ToolBarControlTypeId or UIA_AppBarControlTypeId => "toolbar",
        UIA_TextControlTypeId => headingLevel > 0 ? "heading" : "text",
        UIA_WindowControlTypeId => "dialog", //窗口里套的窗口：对话框
        _ => "group",
    };

    private static UiaElement Wrap(IUIAutomationElement element, int type) => new(element, type)
    {
        CanInvoke = Flag(element, UIA_IsInvokePatternAvailablePropertyId),
        CanToggle = Flag(element, UIA_IsTogglePatternAvailablePropertyId),
        CanSelect = Flag(element, UIA_IsSelectionItemPatternAvailablePropertyId),
        CanExpand = Flag(element, UIA_IsExpandCollapsePatternAvailablePropertyId),
        HasDefaultAction = Flag(element, UIA_IsLegacyIAccessiblePatternAvailablePropertyId),
    };

    private static string CheckedState(int? state) => state switch
    {
        ToggleOn => "true",
        ToggleIndeterminate => "mixed",
        _ => "false",
    };

    private static AxNode More(int count) => new("text", $"… {count} more items not shown");

    // 不支持的属性读回来是一个占位对象，按类型认就自然略过了
    private static bool Flag(IUIAutomationElement element, int property) => element.GetCachedPropertyValue(property) is true;

    private static int? Number(IUIAutomationElement element, int property) =>
        element.GetCachedPropertyValue(property) is int number ? number : null;

    private static string Text(IUIAutomationElement element, int property) =>
        element.GetCachedPropertyValue(property) as string ?? string.Empty;
}
