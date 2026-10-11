/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Tools.Accessibility;

/// <summary>
/// 无障碍角色的分类口径，快照器与按字查找共用一份
/// </summary>
internal static partial class AxRoles
{
    /// <summary>从屏幕图上认出的一行字（桌面的 OCR 层）：没有无障碍元素可调，点它就是点它框的中心</summary>
    public const string OcrText = "ocr-text";

    // 能点、能填、能选的角色才编号；标题、段落只读，编了号模型也无事可做
    private static readonly HashSet<string> Actionable = new(StringComparer.OrdinalIgnoreCase)
    {
        "button", "link", "textbox", "searchbox", "combobox", "listbox", "option", "checkbox", "radio",
        "switch", "slider", "spinbutton", "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "treeitem",
        OcrText,
    };

    // 没名字时本身不成行、子节点上提一层的容器
    private static readonly HashSet<string> Transparent = new(StringComparer.OrdinalIgnoreCase)
    {
        "RootWebArea", "WebArea", "generic", "none", "presentation", "group", "Section", "MenuListPopup",
    };

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "InlineTextBox", "LineBreak",
    };

    // 页面超长时在精简版里收起的区域：导航、侧栏、页脚多是成百的链接，正文才是要看的
    private static readonly HashSet<string> Collapsible = new(StringComparer.OrdinalIgnoreCase)
    {
        "navigation", "complementary", "contentinfo",
    };

    /// <summary>可操作、该编号</summary>
    public static bool IsActionable(AxNode node) => Actionable.Contains(node.Role) && !node.Disabled;

    /// <summary>没名字时不成行的容器</summary>
    public static bool IsTransparent(AxNode node) => string.IsNullOrWhiteSpace(node.Name) && Transparent.Contains(node.Role);

    /// <summary>整个跳过（排版用的内部节点）</summary>
    public static bool IsSkipped(AxNode node) => Skipped.Contains(node.Role);

    /// <summary>精简版里收起</summary>
    public static bool IsCollapsible(AxNode node) => Collapsible.Contains(node.Role);

    /// <summary>一段文字</summary>
    public static bool IsText(AxNode node) =>
        string.Equals(node.Role, "StaticText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.Role, "text", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 去掉名字的回声：链接、按钮的名字由里面的几段字拼成（「Issues (161)」下挂着「Issues」「(」「161」「)」），
    /// 名字里已有的字不再成行（桌面各平台读树共用）
    /// </summary>
    /// <param name="node">父节点</param>
    /// <param name="children">它的子节点（就地删）</param>
    /// <returns>同一个列表</returns>
    public static List<AxNode> WithoutEchoes(AxNode node, List<AxNode> children)
    {
        string name = node.Name;
        if (name.Trim().Length == 0) return children;
        children.RemoveAll(child => child.Role == "text" && child.Children.Count == 0 && child.Name.Trim() is { Length: > 0 } text &&
                                    (name.Contains(text, StringComparison.Ordinal) || text.All(char.IsPunctuation)));
        return children;
    }

    /// <summary>空白折成一个空格并去掉首尾</summary>
    public static string Flatten(string text) => text.Length == 0 ? text : WhitespaceRun().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
