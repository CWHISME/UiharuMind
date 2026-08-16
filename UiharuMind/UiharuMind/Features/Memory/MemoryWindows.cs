/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Memory;

/// <summary>
/// 知识库相关窗口的打开入口。
///
/// 住在本 feature 而不是 <see cref="UIManager"/>：那里是通用的窗口栈与生命周期机制，
/// 不该知道这四个具体窗口。窗口栈本身仍由 <c>ShowDialogStackWindow</c> 提供。
/// </summary>
public static class MemoryWindows
{
    /// <summary>
    /// 打开知识库选择窗
    /// </summary>
    /// <param name="owner">宿主窗口</param>
    /// <param name="onSelectMemory">选中回调</param>
    /// <param name="selectedMemory">当前已选中的知识库</param>
    public static void ShowMemorySelectWindow(Window owner, Action<MemoryData>? onSelectMemory,
        MemoryData? selectedMemory)
    {
        var window = new MemorySelectWindow();
        window.DataContext = new MemorySelectWindowModel(selectedMemory, onSelectMemory, window.Close);
        window.ShowDialogStackWindow(owner);
    }

    /// <summary>
    /// 打开知识库编辑窗
    /// </summary>
    /// <param name="owner">宿主窗口</param>
    /// <param name="memoryData">目标知识库</param>
    /// <param name="onClose">关闭回调</param>
    public static void ShowMemoryEditorWindow(Window owner, MemoryData memoryData, Action? onClose = null)
    {
        var window = new MemoryEditorWindow
        {
            DataContext = new MemoryEditorWindowModel(memoryData, onClose)
        };
        window.ShowDialogStackWindow(owner);
    }

    /// <summary>
    /// 打开文本条目编辑窗
    /// </summary>
    /// <param name="owner">宿主窗口</param>
    /// <param name="source">待编辑的条目；传 null 即新建</param>
    /// <returns>编辑结果；取消为 null</returns>
    public static async Task<MemoryTextSource?> ShowMemoryTextSourceEditWindow(
        Window owner, MemoryTextSource? source = null)
    {
        var window = new MemoryTextSourceEditWindow
        {
            DataContext = new MemoryTextSourceEditWindowModel(source)
        };
        return await window.ShowDialog<MemoryTextSource?>(owner);
    }

    /// <summary>
    /// 打开新建知识库窗
    /// </summary>
    /// <param name="owner">宿主窗口</param>
    /// <returns>创建请求；取消为 null</returns>
    public static async Task<MemoryCreateRequest?> ShowMemoryCreateWindow(Window owner)
    {
        var window = new MemoryCreateWindow
        {
            DataContext = new MemoryCreateWindowModel()
        };
        return await window.ShowDialog<MemoryCreateRequest?>(owner);
    }
}
