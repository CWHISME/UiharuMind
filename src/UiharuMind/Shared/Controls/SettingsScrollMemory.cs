using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 设置页滚动位置的跨会话记忆（附件属性用法）：
/// <code>
/// &lt;ScrollViewer att:SettingsScrollMemory.ScrollKey="GeneralSetting"&gt; … &lt;/ScrollViewer&gt;
/// </code>
/// 挂上后两件事：
/// <list type="number">
/// <item>加载时（若当前位置还在顶部）把上次的位置滚回去；</item>
/// <item>滚动时把当前位置写进 <see cref="SettingConfig.SettingsScrollPositions"/>（内存）。</item>
/// </list>
/// 落盘不在这里做：设置窗口关窗时 <c>SettingsWindow.OnPreClose</c> 会 Save 整份
/// <see cref="SettingConfig"/>，字典随它一起落盘。缓存窗口复用时位置本来就在，
/// 这一层只管跨会话；「只在顶部才还原」保证缓存复用不会被误覆盖。
/// </summary>
public class SettingsScrollMemory
{
    /// <summary>给 ScrollViewer 挂的滚动记忆 key（页面标题 key 即可，全窗内唯一）</summary>
    public static readonly AttachedProperty<string?> ScrollKeyProperty =
        AvaloniaProperty.RegisterAttached<SettingsScrollMemory, ScrollViewer, string?>(
            nameof(ScrollKeyProperty), null);

    // 当前挂着的 key：事件回调时读它取记忆槽；key 改了只换槽、不重复接线
    private static readonly ConditionalWeakTable<ScrollViewer, string> Keys = new();

    static SettingsScrollMemory()
    {
        ScrollKeyProperty.Changed.AddClassHandler<ScrollViewer>(OnScrollKeyChanged);
    }

    /// <summary>设置滚动记忆 key</summary>
    /// <param name="scrollViewer">目标滚动容器</param>
    /// <param name="key">记忆 key</param>
    public static void SetScrollKey(ScrollViewer scrollViewer, string? key) =>
        scrollViewer.SetValue(ScrollKeyProperty, key);

    /// <summary>取滚动记忆 key</summary>
    /// <param name="scrollViewer">目标滚动容器</param>
    /// <returns>记忆 key</returns>
    public static string? GetScrollKey(ScrollViewer scrollViewer) =>
        scrollViewer.GetValue(ScrollKeyProperty);

    private static void OnScrollKeyChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string key) return;

        if (Keys.TryGetValue(scrollViewer, out _))
        {
            Keys.Remove(scrollViewer);
            Keys.Add(scrollViewer, key);
            return;
        }

        Keys.Add(scrollViewer, key);
        scrollViewer.Loaded += (_, _) => Restore(scrollViewer);
        scrollViewer.ScrollChanged += (_, _) => Save(scrollViewer);
        // 关窗/换页摘挂时再记一笔，兜住没触发 ScrollChanged 的位置变化
        scrollViewer.DetachedFromVisualTree += (_, _) => Save(scrollViewer);
    }

    private static void Restore(ScrollViewer scrollViewer)
    {
        if (!Keys.TryGetValue(scrollViewer, out string? key)
            || !ConfigManager.Instance.Setting.SettingsScrollPositions.TryGetValue(key, out double stored)
            || stored <= 0)
        {
            return;
        }

        // 等这一轮布局把 Extent/Viewport 量出来再滚，否则按旧尺寸夹出来的值不准
        Dispatcher.UIThread.Post(() =>
        {
            // 已经不在顶部就不动它：缓存窗口复用/用户刚手动滚过都算
            if (scrollViewer.Offset.Y > 0.5) return;
            double max = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            scrollViewer.Offset = new Avalonia.Vector(0, Math.Min(stored, max));
        }, DispatcherPriority.Loaded);
    }

    private static void Save(ScrollViewer scrollViewer)
    {
        if (!Keys.TryGetValue(scrollViewer, out string? key) || scrollViewer.Offset.Y <= 0) return;
        ConfigManager.Instance.Setting.SettingsScrollPositions[key] = scrollViewer.Offset.Y;
    }
}
