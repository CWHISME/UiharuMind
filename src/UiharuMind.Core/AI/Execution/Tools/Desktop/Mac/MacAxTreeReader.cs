/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Globalization;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using static UiharuMind.Core.AI.Execution.Tools.Desktop.Mac.MacNative;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;

/// <summary>
/// 把一个窗口的 AX 树读成 <see cref="AxNode"/>：每个元素一次跨进程往返读齐一批属性，
/// 角色映射成与网页同一套名字，快照器、对账器、按字查找就都能照用。
/// 应用的菜单栏挂在窗口后面：顶层菜单都列出来，展开着的那个才往下读
/// </summary>
internal sealed class MacAxTreeReader
{
    private const int MaxDepth = 40;
    private const int MaxChildren = 300; //一个列表、表格最多读这么多项，余下的列成一行

    // 一次读齐的属性，顺序即下标
    private static readonly string[] Attributes =
    [
        "AXRole", "AXSubrole", "AXTitle", "AXDescription", "AXValue", "AXHelp", "AXEnabled", "AXSelected",
        "AXExpanded", "AXPosition", "AXSize", "AXURL", "AXPlaceholderValue", "AXChildren",
    ];

    private const int ChildrenIndex = 13;

    private static readonly Lazy<IntPtr> AttributeArray = new(() => CreateArray(Attributes.Select(Cf).ToArray()));

    // 只是滚动条、分隔条之类，没有内容也不用点
    private static readonly HashSet<string> SkippedRoles =
    [
        "AXScrollBar", "AXValueIndicator", "AXSplitter", "AXGrowArea", "AXRuler", "AXRulerMarker", "AXColumn", "AXMatte",
    ];

    // Chromium 给网页里每个元素都报「收起」：只有这两类的展开状态有意义
    private static readonly HashSet<string> Expandable = ["combobox", "treeitem"];

    private readonly MacAxElement? _focused; //应用此刻的焦点：各元素自报的 AXFocused 不可靠（列表里的格子个个说自己有焦点）
    private int _budget;

    private MacAxTreeReader(int maxNodes, MacAxElement? focused)
    {
        _budget = maxNodes;
        _focused = focused;
    }

    /// <summary>
    /// 读一个窗口
    /// </summary>
    /// <param name="window">窗口元素</param>
    /// <param name="app">应用元素（读菜单栏）</param>
    /// <param name="title">窗口标题（作根的名字）</param>
    /// <param name="maxNodes">最多读多少个节点</param>
    /// <returns>树，根是窗口</returns>
    public static AxNode Read(MacAxElement window, MacAxElement app, string title, int maxNodes)
    {
        MacAxTreeReader reader = new(maxNodes, app.GetElement("AXFocusedUIElement"));
        List<AxNode> children = new();
        if (AXUIElementCopyAttributeValue(window.Handle, Cf("AXChildren"), out IntPtr items) == AxSuccess)
        {
            try
            {
                children = reader.ReadChildren(items, "AXWindow", 1, inWebArea: false);
            }
            finally
            {
                CFRelease(items);
            }
        }

        if (app.GetElement("AXMenuBar") is { } menuBar && reader.ReadNode(menuBar.Handle, "AXApplication", 1, false) is { } menu)
        {
            children.Add(menu);
        }

        return new AxNode("window", title) { Children = children, Source = window };
    }

    // 子元素的句柄借自父元素那批属性的数组，读完父元素才释放
    private List<AxNode> ReadChildren(IntPtr array, string role, int depth, bool inWebArea)
    {
        List<AxNode> children = new();
        if (depth > MaxDepth) return children;

        IntPtr[] items = ArrayItems(array);
        int shown = Math.Min(items.Length, MaxChildren);
        for (int i = 0; i < shown; i++)
        {
            if (_budget <= 0)
            {
                children.Add(More(items.Length - i));
                return children;
            }

            if (IsElement(items[i]) && ReadNode(items[i], role, depth, inWebArea) is { } child) children.Add(child);
        }

        if (items.Length > shown) children.Add(More(items.Length - shown));
        return children;
    }

    private AxNode? ReadNode(IntPtr element, string parentRole, int depth, bool inWebArea)
    {
        if (AXUIElementCopyMultipleAttributeValues(element, AttributeArray.Value, 0, out IntPtr batch) != AxSuccess) return null;
        try
        {
            IntPtr[] raw = ArrayItems(batch);
            object?[] values = raw.Select(ToManaged).ToArray();
            string role = Text(values, 0);
            if (role.Length == 0 || SkippedRoles.Contains(role)) return null;
            _budget--;

            bool webArea = inWebArea || role == "AXWebArea";
            AxNode node = Describe(role, Text(values, 1), parentRole, values) with
            {
                Source = MacAxElement.Retain(element, role, webArea),
                Focused = _focused != null && CFEqual(element, _focused.Handle) != 0,
            };

            // 没名字的图（文件图标、装饰）只占行
            if (node.Role == "img" && node.Name.Trim().Length == 0) return null;

            // 关着的菜单不往下读：每个顶层菜单下都挂着整份菜单项，全读进来快照就淹了
            bool closedMenu = role == "AXMenuBarItem" && !node.Selected;
            IntPtr children = raw.Length > ChildrenIndex ? raw[ChildrenIndex] : IntPtr.Zero;
            return closedMenu || !IsArray(children) ? node : node with { Children = AxRoles.WithoutEchoes(node, ReadChildren(children, role, depth + 1, webArea)) };
        }
        finally
        {
            CFRelease(batch);
        }
    }

    private static AxNode Describe(string role, string subrole, string parentRole, object?[] values)
    {
        string title = Text(values, 2);
        string description = Text(values, 3);
        object? value = values[4];
        string mapped = MapRole(role, subrole, parentRole);
        bool isText = mapped == "text";
        string name = isText ? FirstNonEmpty(title, AsText(value), description)
            : FirstNonEmpty(title, description, WindowButtonName(subrole));

        AxRect? bounds = values[9] is CGPoint position && values[10] is CGPoint size
            ? new AxRect(position.X, position.Y, size.X, size.Y)
            : null;
        bool toggle = mapped is "checkbox" or "radio" or "switch";
        bool tab = mapped == "tab";
        bool heading = mapped == "heading";
        return new AxNode(mapped, name)
        {
            Value = isText || toggle || tab || heading ? string.Empty : AsText(value),
            Level = heading && value is double level ? (int)level : 0,
            Checked = toggle ? CheckedState(value) : string.Empty,
            Selected = values[7] is true || (tab && value is 1d),
            Disabled = values[6] is false,
            Expanded = mapped == "combobox" && role == "AXPopUpButton" ? false
                : Expandable.Contains(mapped) ? values[8] as bool? : null,
            Secret = subrole == "AXSecureTextField",
            Description = FirstNonEmpty(Text(values, 5), Text(values, 12), name.Length > 0 ? string.Empty : description),
            Url = Text(values, 11) is var url && url.StartsWith("file:///.file/", StringComparison.Ordinal) ? string.Empty : url, //访达的文件引用地址认不出是什么
            Bounds = bounds,
        };
    }

    // 与网页同一套角色名：AxRoles 据此决定编不编号、成不成行
    private static string MapRole(string role, string subrole, string parentRole) => role switch
    {
        "AXButton" or "AXMenuButton" or "AXDisclosureTriangle" or "AXColorWell" or "AXDockItem" => "button",
        "AXPopUpButton" or "AXComboBox" => "combobox",
        "AXTextField" when subrole == "AXSearchField" => "searchbox",
        "AXTextField" or "AXTextArea" or "AXDateField" or "AXTimeField" => "textbox",
        "AXCheckBox" when subrole == "AXSwitch" => "switch",
        "AXCheckBox" when subrole == "AXToggle" => "button",
        "AXCheckBox" => "checkbox",
        "AXRadioButton" when parentRole == "AXTabGroup" => "tab",
        "AXRadioButton" => "radio",
        "AXSlider" => "slider",
        "AXIncrementor" => "spinbutton",
        "AXLink" => "link",
        "AXMenuItem" or "AXMenuBarItem" => "menuitem",
        "AXMenuBar" => "menubar",
        "AXMenu" => "menu",
        "AXRow" when parentRole == "AXOutline" => "treeitem",
        "AXRow" => "option",
        "AXCell" => "cell",
        "AXStaticText" => "text",
        "AXImage" => "img",
        "AXHeading" => "heading",
        "AXToolbar" => "toolbar",
        "AXTabGroup" => "tablist",
        "AXList" => "list",
        "AXTable" => "table",
        "AXOutline" => "tree",
        "AXWebArea" => "WebArea",
        "AXSheet" => "dialog",
        "AXWindow" => "window",
        "AXProgressIndicator" or "AXBusyIndicator" or "AXLevelIndicator" => "progressbar",
        _ => "group",
    };

    // 交通灯按钮没有名字，只有子角色
    private static string WindowButtonName(string subrole) => subrole switch
    {
        "AXCloseButton" => "close",
        "AXMinimizeButton" => "minimize",
        "AXZoomButton" => "zoom",
        "AXFullScreenButton" => "full screen",
        _ => string.Empty,
    };

    private static string CheckedState(object? value) => value switch
    {
        1d => "true",
        2d => "mixed",
        _ => "false",
    };

    private static AxNode More(int count) => new("text", $"… {count} more items not shown");

    private static string Text(object?[] values, int index) => index < values.Length ? AsText(values[index]) : string.Empty;

    private static string AsText(object? value) => value switch
    {
        string text => text,
        double number => number.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private static string FirstNonEmpty(params string[] texts) => texts.FirstOrDefault(text => text.Trim().Length > 0) ?? string.Empty;
}
