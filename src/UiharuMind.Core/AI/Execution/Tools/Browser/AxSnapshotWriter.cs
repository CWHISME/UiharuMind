/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 一次快照的产物
/// </summary>
/// <param name="Text">完整快照：缩进文本，可操作的元素带 [ref=eN]</param>
/// <param name="CompactText">精简版：导航、侧栏、页脚各收成一行，编号与完整版一致。页面超出预算时内联给的是这份</param>
/// <param name="Refs">编号 → 节点</param>
/// <param name="PickerOptions">收起的原生下拉框里的选项 → 所属下拉框（按引用比对）。这些选项没有位置、点不着，
/// 动作落到它们身上时改为在下拉框上选这一项</param>
public sealed record AxSnapshot(string Text, string CompactText, IReadOnlyDictionary<string, AxNode> Refs,
    IReadOnlyDictionary<AxNode, AxNode> PickerOptions);

/// <summary>
/// 把无障碍树写成给模型看的缩进文本，形状仿 Playwright 的 AI 快照：
/// 每行 <c>- 角色 "名称" [状态] [ref=eN]: 值</c>，只给能操作的元素编号。
/// 纯函数，不碰浏览器；编号只对产出它的那一份快照有效。
/// </summary>
public static class AxSnapshotWriter
{
    private const int MaxNameLength = 120;
    private const int MaxTextLength = 4000; //正文按段给足；整页超预算由落盘兜
    private const string TextPrefix = "- text: ";
    private const string SecretMask = "••••••";

    /// <summary>
    /// 写一份快照
    /// </summary>
    /// <param name="root">树根（通常是 RootWebArea，它的名字是页面标题，不成行）</param>
    /// <returns>完整版、精简版与编号表</returns>
    public static AxSnapshot Write(AxNode root)
    {
        Printer printer = new();
        printer.WriteChildren(root, new Scope(0, string.Empty, string.Empty, false, null));
        return new AxSnapshot(DropEchoedLabels(printer.Full), DropEchoedLabels(printer.Compact), printer.Refs,
            printer.PickerOptions);
    }

    private static string RenderLine(AxNode node, string name, string value, string? refId)
    {
        StringBuilder line = new("- ");
        line.Append(node.Role);
        if (name.Length > 0) line.Append(" \"").Append(name).Append('"');
        // 没名字的图标按钮、链接只剩一个编号，模型认不出是什么：给出描述或去处
        else if (refId != null && Clip(node.Description, MaxNameLength) is { Length: > 0 } description)
            line.Append(" [description=\"").Append(description).Append("\"]");
        else if (refId != null && Clip(node.Url, MaxNameLength) is { Length: > 0 } url)
            line.Append(" [url=").Append(url).Append(']');
        if (node.Level > 0) line.Append(" [level=").Append(node.Level).Append(']');
        if (node.Secret) line.Append(" [password]");
        if (node.Checked == "true") line.Append(" [checked]");
        else if (node.Checked == "mixed") line.Append(" [checked=mixed]");
        if (node.Selected) line.Append(" [selected]");
        if (node.Pressed) line.Append(" [pressed]");
        if (node.Expanded is { } expanded) line.Append(expanded ? " [expanded]" : " [collapsed]");
        if (node.Disabled) line.Append(" [disabled]");
        if (refId != null) line.Append(" [ref=").Append(refId).Append(']');
        // 密码只说填没填：Chrome 本就给圆点，长度也不必透出去
        if (value.Length > 0) line.Append(": ").Append(node.Secret ? SecretMask : value);
        return line.ToString();
    }

    // 表单里标签那段字紧挨着输入框，而输入框的名字就是它：只留输入框那一行
    private static string DropEchoedLabels(string snapshot)
    {
        string[] lines = snapshot.Split('\n');
        StringBuilder kept = new(snapshot.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!(IsEchoedLabel(lines, i, i - 1) || IsEchoedLabel(lines, i, i + 1))) kept.Append(lines[i]).Append('\n');
        }

        return kept.ToString().TrimEnd();
    }

    private static bool IsEchoedLabel(string[] lines, int textIndex, int neighborIndex)
    {
        if (neighborIndex < 0 || neighborIndex >= lines.Length) return false;
        string line = lines[textIndex];
        int indent = line.Length - line.TrimStart(' ').Length;
        if (string.CompareOrdinal(line, indent, TextPrefix, 0, TextPrefix.Length) != 0) return false;

        string label = line[(indent + TextPrefix.Length)..];
        string neighbor = lines[neighborIndex];
        return neighbor.Length > indent + 2 && neighbor[..indent].Trim().Length == 0 &&
               string.CompareOrdinal(neighbor, indent, "- ", 0, 2) == 0 &&
               !neighbor.AsSpan(indent).StartsWith(TextPrefix) &&
               neighbor.Contains($" \"{label}\"", StringComparison.Ordinal);
    }

    // 透过无名容器往下看，恰好只有一段字时返回它
    private static string? SoleText(AxNode node)
    {
        AxNode? only = null;
        foreach (AxNode child in VisibleChildren(node))
        {
            if (only != null) return null;
            only = child;
        }

        return only != null && AxRoles.IsText(only) ? Clip(only.Name, MaxTextLength) : null;
    }

    private static IEnumerable<AxNode> VisibleChildren(AxNode node)
    {
        foreach (AxNode child in node.Children)
        {
            if (AxRoles.IsSkipped(child)) continue;
            if (!AxRoles.IsText(child) && AxRoles.IsTransparent(child))
            {
                foreach (AxNode grandChild in VisibleChildren(child)) yield return grandChild;
            }
            else
            {
                yield return child;
            }
        }
    }

    private static string Clip(string text, int maxLength)
    {
        string flat = AxRoles.Flatten(text);
        return flat.Length <= maxLength ? flat : flat[..maxLength] + "…";
    }

    /// <summary>
    /// 往下一层带的东西：缩进、父节点的名字与值（子节点里重复它们的那段字省掉）、是否不再编号、身在哪个收起的下拉框里
    /// </summary>
    private readonly record struct Scope(int Depth, string ParentName, string ParentValue, bool NoRefs, AxNode? Picker);

    /// <summary>
    /// 一次遍历同时写完整版与精简版：进了可收起的区域，后面的行只进完整版，精简版在区域头下补一行「收起了几行」
    /// </summary>
    private sealed class Printer
    {
        private readonly StringBuilder _full = new();
        private readonly StringBuilder _compact = new();
        private int _fullLines;
        private int _collapsedDepth;

        public Dictionary<string, AxNode> Refs { get; } = new();

        public Dictionary<AxNode, AxNode> PickerOptions { get; } = new(ReferenceEqualityComparer.Instance);

        public string Full => _full.ToString().TrimEnd();

        public string Compact => _compact.ToString().TrimEnd();

        public void WriteChildren(AxNode node, Scope scope)
        {
            foreach (AxNode child in node.Children) WriteNode(child, scope);
        }

        private void WriteNode(AxNode node, Scope scope)
        {
            if (AxRoles.IsSkipped(node)) return;

            if (AxRoles.IsText(node))
            {
                // 链接、按钮、标题的名字与输入框的值本就来自里面那段字，再写一行是重复
                string content = Clip(node.Name, MaxTextLength);
                if (content.Length > 0 && content != scope.ParentName && content != scope.ParentValue)
                {
                    Line(scope.Depth, TextPrefix + content);
                }

                return;
            }

            if (AxRoles.IsTransparent(node))
            {
                WriteChildren(node, scope);
                return;
            }

            if (scope.Picker != null && string.Equals(node.Role, "option", StringComparison.OrdinalIgnoreCase))
            {
                PickerOptions[node] = scope.Picker;
            }

            string name = Clip(node.Name, MaxNameLength);
            string? refId = null;
            if (AxRoles.IsActionable(node) && !scope.NoRefs)
            {
                refId = "e" + (Refs.Count + 1);
                Refs[refId] = node;
            }

            string value = Clip(node.Value, MaxTextLength);
            if (value == name) value = string.Empty;

            // 没名字、里面只有一段字的（段落、列表项）收成一行
            if (value.Length == 0 && name.Length == 0 && SoleText(node) is { } sole)
            {
                Line(scope.Depth, RenderLine(node, name, sole, refId));
                return;
            }

            Line(scope.Depth, RenderLine(node, name, value, refId));

            // 收起的原生下拉框里的选项点不着（没有位置），只列出来给 select 按文字选
            bool closedPicker = string.Equals(node.Role, "combobox", StringComparison.OrdinalIgnoreCase) &&
                                node.Expanded == false;
            Scope inner = new(scope.Depth + 1, name, value, scope.NoRefs || closedPicker,
                closedPicker ? node : scope.Picker);
            if (!AxRoles.IsCollapsible(node))
            {
                WriteChildren(node, inner);
                return;
            }

            int linesBefore = _fullLines;
            _collapsedDepth++;
            WriteChildren(node, inner);
            _collapsedDepth--;
            int hidden = _fullLines - linesBefore;
            if (hidden > 0 && _collapsedDepth == 0)
            {
                Indent(_compact, inner.Depth).Append("- … ").Append(hidden).Append(" lines collapsed\n");
            }
        }

        private void Line(int depth, string content)
        {
            Indent(_full, depth).Append(content).Append('\n');
            _fullLines++;
            if (_collapsedDepth == 0) Indent(_compact, depth).Append(content).Append('\n');
        }

        private static StringBuilder Indent(StringBuilder text, int depth) => text.Append(' ', depth * 2);
    }
}
