/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Accessibility;

/// <summary>
/// 无障碍树上的一个节点，与来源无关（网页、桌面窗口）：快照器只认它，引擎实现负责从自家的树转过来，
/// 并在 <see cref="Source"/> 里留住找回真实元素的门路。
/// </summary>
/// <param name="Role">角色（button、link、textbox、StaticText…）</param>
/// <param name="Name">可访问名称</param>
public sealed record AxNode(string Role, string Name)
{
    /// <summary>当前值（输入框里的字、下拉框的选中项）</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>勾选状态：true / false / mixed；不可勾选为空</summary>
    public string Checked { get; init; } = string.Empty;

    /// <summary>是否选中（选项、标签页）</summary>
    public bool Selected { get; init; }

    /// <summary>是否禁用</summary>
    public bool Disabled { get; init; }

    /// <summary>是否有键盘焦点（按键、打字落到它身上）</summary>
    public bool Focused { get; init; }

    /// <summary>是否按下（切换按钮）</summary>
    public bool Pressed { get; init; }

    /// <summary>展开状态；不可展开为 null</summary>
    public bool? Expanded { get; init; }

    /// <summary>标题层级；不是标题为 0</summary>
    public int Level { get; init; }

    /// <summary>是否密码框：快照里标出来、值不给看，动作层也不往里打字（密码只经用户的手）</summary>
    public bool Secret { get; init; }

    /// <summary>无障碍描述（aria-describedby、title）：没名字的图标按钮只能靠它认</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>链接地址：没名字的图标链接只能靠它认</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>屏幕上的位置（逻辑坐标，左上为原点）；引擎给不出为 null</summary>
    public AxRect? Bounds { get; init; }

    /// <summary>子节点</summary>
    public IReadOnlyList<AxNode> Children { get; init; } = [];

    /// <summary>引擎侧的原始节点，动作时据此找回元素；快照器不碰它</summary>
    public object? Source { get; init; }

    /// <summary>写给模型看的称呼，如 <c>button "提交"</c>；没名字只剩角色</summary>
    public string Label => Name.Trim() is { Length: > 0 } name ? $"{Role} \"{name}\"" : Role;
}

/// <summary>
/// 屏幕上的一块矩形（逻辑坐标）
/// </summary>
/// <param name="X">左</param>
/// <param name="Y">上</param>
/// <param name="Width">宽</param>
/// <param name="Height">高</param>
public readonly record struct AxRect(double X, double Y, double Width, double Height)
{
    /// <summary>中心点横坐标</summary>
    public double CenterX => X + Width / 2;

    /// <summary>中心点纵坐标</summary>
    public double CenterY => Y + Height / 2;

    /// <summary>是否有面积</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// 是否包含某点
    /// </summary>
    /// <param name="x">横坐标</param>
    /// <param name="y">纵坐标</param>
    /// <returns>包含为 true</returns>
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}
