using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.VisualTree;
using LiveMarkdown.Avalonia;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 垫片：LiveMarkdown 2.4.3 在按下鼠标那一刻把选区作用域里的文字块存成快照（私有字段 <c>_activeScopeBlocks</c>），
/// 整个拖动期间不再刷新——中途新建出来的块不在快照里，拖过去选不上，松手重拖才接得上。
/// 这里替正处于按下交互中的渲染器按库的同一口径重取快照。
///
/// 依赖库的私有成员：取不到就记一次警告、什么都不做（退回原行为）。上游修好后整个删掉。
/// </summary>
internal static class MarkdownSelectionSnapshotShim
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? ScopeBlocksField =
        typeof(MarkdownRenderer).GetField("_activeScopeBlocks", InstanceNonPublic);

    internal static readonly bool IsAvailable = ScopeBlocksField?.FieldType == typeof(MarkdownTextBlock[]);

    private static bool _warned;

    /// <summary>
    /// 在 <paramref name="renderers"/> 里找到正处于按下交互中的那个（快照非空：按下即取、松开即清，
    /// 包括还没拖过阈值的那一段），把它的快照换成 <paramref name="scopeRoot"/> 下此刻的全部文字块
    /// </summary>
    /// <param name="scopeRoot">选区作用域根（标了 IsSelectionScope 的那个）</param>
    /// <param name="renderers">作用域里的渲染器</param>
    public static void RefreshActive(Visual scopeRoot, IEnumerable<MarkdownRenderer> renderers)
    {
        if (!IsAvailable)
        {
            if (_warned) return;
            _warned = true;
            Log.Warning("LiveMarkdown internals changed, cross-chunk drag selection shim disabled");
            return;
        }

        foreach (MarkdownRenderer renderer in renderers)
        {
            if (ScopeBlocksField!.GetValue(renderer) is null) continue;
            ScopeBlocksField!.SetValue(renderer, CollectVisibleBlocks(scopeRoot).ToArray());
            return;
        }
    }

    // 同库里 GetVisibleMarkdownTextBlockDescendants：先序遍历，跳过不可见子树，收可见的文字块
    private static List<MarkdownTextBlock> CollectVisibleBlocks(Visual root)
    {
        List<MarkdownTextBlock> blocks = new();
        Collect(root, blocks);
        return blocks;
    }

    private static void Collect(Visual visual, List<MarkdownTextBlock> blocks)
    {
        if (!visual.IsEffectivelyVisible) return;
        if (visual is MarkdownTextBlock { IsVisible: true } block) blocks.Add(block);
        foreach (Visual child in visual.GetVisualChildren()) Collect(child, blocks);
    }
}
