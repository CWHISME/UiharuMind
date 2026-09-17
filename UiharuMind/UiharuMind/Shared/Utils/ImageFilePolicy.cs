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

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 图片文件判定：自家贴图窗能吃的扩展名。markdown 链接、文本窗 fallback 两处共用。
/// <b>按扩展名而非探测文件头</b>：点击要立刻有反应，
/// 而读一遍文件头再决定开哪个窗口，在网络盘上就是一次可感知的卡顿。
/// </summary>
public static class ImageFilePolicy
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    /// <summary>扩展名是否在贴图窗可开的名单里</summary>
    public static bool IsImage(string? fullPath)
    {
        string ext = Path.GetExtension(fullPath ?? string.Empty);
        if (ext.Length == 0) return false;
        return Array.IndexOf(ImageExtensions, ext.ToLowerInvariant()) >= 0;
    }
}
