/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 无障碍树上的一个节点，与浏览器引擎无关：快照器只认它，引擎实现负责从自家的树转过来，
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

    /// <summary>子节点</summary>
    public IReadOnlyList<AxNode> Children { get; init; } = [];

    /// <summary>引擎侧的原始节点，动作时据此找回元素；快照器不碰它</summary>
    public object? Source { get; init; }

    /// <summary>写给模型看的称呼，如 <c>button "提交"</c>；没名字只剩角色</summary>
    public string Label => Name.Trim() is { Length: > 0 } name ? $"{Role} \"{name}\"" : Role;
}
