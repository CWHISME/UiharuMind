using System;
using Avalonia.Controls;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.WorldBooks;

/// <summary>
/// 世界书相关窗口的打开入口（镜像 MemoryWindows：窗口栈由 <c>ShowDialogStackWindow</c> 提供）
/// </summary>
public static class WorldBookWindows
{
    /// <summary>
    /// 打开世界书选择窗：选一本挂载，或新建一本后直接挂载
    /// </summary>
    /// <param name="owner">宿主窗口</param>
    /// <param name="onSelectName">选中/新建的书名回调</param>
    /// <param name="selectedName">当前已挂载的书名（列表里高亮）</param>
    public static void ShowSelectWindow(Window owner, Action<string>? onSelectName, string selectedName)
    {
        var window = new WorldBookSelectWindow(onSelectName, selectedName);
        window.ShowDialogStackWindow(owner);
    }

    /// <summary>
    /// 打开世界书编辑窗：改共享书的条目与预算
    /// </summary>
    /// <param name="owner">宿主窗口</param>
    /// <param name="bookName">要编辑的书名</param>
    public static void ShowEditorWindow(Window owner, string bookName)
    {
        var window = new WorldBookEditorWindow(bookName);
        window.ShowDialogStackWindow(owner);
    }
}