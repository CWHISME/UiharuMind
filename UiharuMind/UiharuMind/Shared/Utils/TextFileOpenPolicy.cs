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
/// 文本文件打开策略：「双击文本文件进自家编辑窗」的准入判定。
/// 只判扩展名 + 大小，内容是否真是文本交给 <see cref="TextFileCodec"/> 在读取时校验——
/// 白名单内偶尔混进一个二进制文件，读出来不是文本会弹错，不会真的被编辑。
/// </summary>
public static class TextFileOpenPolicy
{
    /// <summary>可进编辑窗的扩展名（小写、不带点）。默认白名单，避免内容嗅探误判二进制</summary>
    public static readonly string[] SupportedExtensions =
    [
        "txt", "md", "markdown", "log", "json", "xml", "yaml", "yml", "ini", "cfg", "csv",
        "cs", "cpp", "h", "py", "js", "ts", "tsx", "css", "html"
    ];

    /// <summary>可编辑的文件大小上限：编辑场景的内存与撤销栈都扛不住超大文件，超过就走系统打开</summary>
    public const long MaxEditBytes = 5 * 1024 * 1024;

    /// <summary>扩展名是否在白名单里</summary>
    public static bool IsSupportedExtension(string? fullPath)
    {
        string ext = Path.GetExtension(fullPath ?? string.Empty).TrimStart('.');
        if (ext.Length == 0) return false;
        return Array.IndexOf(SupportedExtensions, ext.ToLowerInvariant()) >= 0;
    }

    /// <summary>文件是否存在且没超过编辑上限</summary>
    public static bool IsWithinEditLimit(string fullPath)
    {
        try
        {
            return File.Exists(fullPath) && new FileInfo(fullPath).Length <= MaxEditBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>双击可进编辑窗的完整判定</summary>
    public static bool IsSupported(string fullPath) => IsSupportedExtension(fullPath) && IsWithinEditLimit(fullPath);
}