/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.IO;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 文本文件打开策略：只判存在 + 大小，<b>不判扩展名</b>——.sh、无扩展名文件都是纯文本，
/// 后缀白名单只会误伤。内容是否真是文本由 <see cref="TextFileCodec"/> 在读取时校验
/// （编码探测 + 控制字符占比），二进制走系统打开，不会真的被编辑。
/// </summary>
public static class TextFileOpenPolicy
{
    /// <summary>可编辑的文件大小上限：编辑场景的内存与撤销栈都扛不住超大文件，超过就走系统打开</summary>
    public const long MaxEditBytes = 5 * 1024 * 1024;

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

    /// <summary>可进编辑窗的完整判定（存在 + 大小；后缀不限）</summary>
    public static bool IsSupported(string fullPath) => IsWithinEditLimit(fullPath);
}