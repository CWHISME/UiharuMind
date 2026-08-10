/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.IO;
using CommunityToolkit.Mvvm.Input;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 工作目录的显示口径。目录名与父路径分两行给，是因为工作区路径普遍很长，
/// 全路径铺开会把侧栏卡片撑到三四行，而用户真正认的是最后那一节。
/// </summary>
public static class WorkspaceDisplay
{
    /// <summary>
    /// 取目录名（路径最后一节）。末尾带分隔符、或本身就是盘符/根目录时回落为整个路径。
    /// </summary>
    /// <param name="path">目录路径</param>
    /// <returns>目录名</returns>
    public static string NameOf(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>
    /// 取父路径，并把 home 前缀折成 <c>~</c>。父目录不存在（根目录）时返回空串。
    /// </summary>
    /// <param name="path">目录路径</param>
    /// <returns>父路径</returns>
    public static string ParentOf(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(parent)) return string.Empty;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0 && parent.StartsWith(home, StringComparison.Ordinal))
        {
            return $"~{parent[home.Length..]}";
        }

        return parent;
    }
}

/// <summary>
/// 最近工作区下拉菜单里的一条。命令随条目一起给出，模板因此不必回头去找 ViewModel。
/// </summary>
/// <param name="Path">工作目录绝对路径</param>
/// <param name="UseCommand">切到该目录</param>
/// <param name="ForgetCommand">把该条从最近列表移除</param>
public sealed record RecentWorkspaceItem(string Path, IRelayCommand UseCommand, IRelayCommand ForgetCommand)
{
    /// <summary>目录名（菜单主文本）</summary>
    public string Name => WorkspaceDisplay.NameOf(Path);

    /// <summary>父路径（菜单副文本）</summary>
    public string Parent => WorkspaceDisplay.ParentOf(Path);
}
