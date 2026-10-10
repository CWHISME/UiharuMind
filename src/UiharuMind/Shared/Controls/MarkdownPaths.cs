using Avalonia;
using Avalonia.Controls;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 会话路径口径的继承附加属性：挂在消息流外层，里面每个 <see cref="SimpleMarkdownViewer"/> 顺着树拿到，
/// 不必经条目视图模型一层层传。没挂的宿主（帮助页、文本窗）维持原样。
/// </summary>
public static class MarkdownPaths
{
    /// <summary>正文地址按哪个会话的口径解析；null 表示不解析简写与相对路径</summary>
    public static readonly AttachedProperty<AgentPathResolver?> ResolverProperty =
        AvaloniaProperty.RegisterAttached<Control, AgentPathResolver?>("Resolver", typeof(MarkdownPaths), inherits: true);

    /// <summary>
    /// 设置路径口径
    /// </summary>
    /// <param name="element">宿主控件</param>
    /// <param name="value">会话的路径口径</param>
    public static void SetResolver(Control element, AgentPathResolver? value) => element.SetValue(ResolverProperty, value);

    /// <summary>
    /// 读取路径口径
    /// </summary>
    /// <param name="element">宿主控件</param>
    /// <returns>会话的路径口径</returns>
    public static AgentPathResolver? GetResolver(Control element) => element.GetValue(ResolverProperty);
}
