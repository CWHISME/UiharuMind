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
/// 编号对账：同一条回复里的几步是并行发出的，模型写的编号都出自它<b>看到的那份</b>快照；
/// 可前面的步骤一动手就重拍、重新编号，后一步的 e21 未必还是原来那个，审批卡翻译的也还是旧的那个。
/// 这里把模型看到的编号翻成此刻页面上的元素：结构没变照用；结构变了只认得回独一份的元素，认不准就拒，宁可让模型重拍。
/// 纯函数，不碰浏览器。
/// </summary>
public static class AxRefReconciler
{
    /// <summary>
    /// 把模型看到的编号翻成此刻的元素
    /// </summary>
    /// <param name="seen">模型写这条回复时看到的编号表</param>
    /// <param name="current">此刻的编号表</param>
    /// <param name="refId">编号</param>
    /// <returns>此刻对应的元素；模型看到的快照里就没有这个编号为 null</returns>
    /// <exception cref="BrowserActionException">结构变了，且元素已不在页上或认不准是哪一个</exception>
    public static AxNode? Find(IReadOnlyDictionary<string, AxNode> seen, IReadOnlyDictionary<string, AxNode> current,
        string refId)
    {
        if (!seen.TryGetValue(refId, out AxNode? wanted)) return null;
        if (ReferenceEquals(seen, current) || SameLayout(seen, current)) return current[refId];

        // 结构变了：同名同角色的在模型看到的那份里不止一个（一列「删除」），删掉一行后编号整体前移，按名字认会认错行
        Signature signature = new(wanted);
        AxNode[] matches = current.Values.Where(node => new Signature(node) == signature).Take(2).ToArray();
        if (matches.Length == 1 && seen.Values.Count(node => new Signature(node) == signature) == 1) return matches[0];

        throw new BrowserActionException(matches.Length == 0
            ? $"{wanted.Label} ({refId}) is no longer on the page: an earlier step in this reply changed it. " +
              "Take a new snapshot and use its refs."
            : $"An earlier step in this reply changed the page, so {refId} ({wanted.Label}) can no longer be " +
              "matched for sure. Take a new snapshot and use its refs.");
    }

    // 编号一一对得上同样的元素（只是填了字、勾了框）：编号仍然有效
    private static bool SameLayout(IReadOnlyDictionary<string, AxNode> seen, IReadOnlyDictionary<string, AxNode> current)
    {
        if (seen.Count != current.Count) return false;
        foreach ((string refId, AxNode node) in seen)
        {
            if (!current.TryGetValue(refId, out AxNode? now) || new Signature(now) != new Signature(node)) return false;
        }

        return true;
    }

    // 认元素只看不随填写变的几样；值、勾选状态会被前面的步骤改掉，不能算进来
    private readonly record struct Signature(string Role, string Name, string Description, string Url)
    {
        public Signature(AxNode node) : this(node.Role, node.Name.Trim(), node.Description, node.Url)
        {
        }
    }
}
