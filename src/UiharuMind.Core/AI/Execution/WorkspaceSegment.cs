/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 工作区路径 → 目录名一段：<c>目录名_短哈希</c>（ADR 0026 起由文件记忆与 agent 产物共用）。
///
/// 两半都不可省。光用目录名会撞（两个项目都叫 <c>client</c>，撞了就是记忆互相污染）；
/// 光用哈希用户在文件管理器里认不出是哪个项目——与 ADR 0002「目录名里带角色名」同一个取舍。
///
/// 哈希取 SHA256 而非 <c>string.GetHashCode</c>：后者每进程随机化，
/// 重启一次就换一个目录，记忆当场"丢"。
/// </summary>
public static class WorkspaceSegment
{
    private const int MaxNameLength = 32; //目录名里目录名部分的长度上限
    private const int HashLength = 8; //哈希部分的十六进制位数

    /// <summary>
    /// 工作区路径 → 目录名一段。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    /// <returns>目录名一段，形如 <c>目录名_短哈希</c></returns>
    public static string From(string workspacePath)
    {
        // 先归一再算:同一个工作区经不同写法(相对路径、大小写、尾斜杠)进来必须落到同一段,
        // 否则同一个项目会分裂成几份(记忆/产物)
        string full = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string canonical = OperatingSystem.IsLinux() ? full : full.ToLowerInvariant();

        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string suffix = Convert.ToHexString(hash)[..HashLength].ToLowerInvariant();

        string name = Sanitize(Path.GetFileName(full), MaxNameLength);
        return name.Length == 0 ? suffix : $"{name}_{suffix}";
    }

    /// <summary>目录名安全化：只留字母与数字（中文属 Letter，会保留），再截到长度上限</summary>
    private static string Sanitize(string text, int maxLength)
    {
        string kept = new(text.Where(char.IsLetterOrDigit).ToArray());
        return kept.Length <= maxLength ? kept : kept[..maxLength];
    }
}
