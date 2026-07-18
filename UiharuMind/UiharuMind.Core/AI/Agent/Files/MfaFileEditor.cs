/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Agents.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 反射转发到 MFA 内部静态类 Microsoft.Agents.AI.FileEditor 的 replace 逻辑,
/// 以保证与 MFA 内置 file_access 工具行为逐字节一致(尤其 replace_lines 的换行/空行删除语义)。
/// FileEditor 为 internal,版本升级若改名会在此处抛明确异常并记日志。
/// </summary>
internal static class MfaFileEditor
{
    private static readonly object? s_lock = new();
    private static Type? s_type;
    private static MethodInfo? s_applyReplace;
    private static MethodInfo? s_applyReplaceLines;
    private static bool s_resolved;

    private static void EnsureResolved()
    {
        if (s_resolved) return;
        lock (s_lock!)
        {
            if (s_resolved) return;

            Type? type = typeof(FileSystemAgentFileStore).Assembly
                .GetType("Microsoft.Agents.AI.FileEditor", throwOnError: false);
            if (type is null)
            {
                s_resolved = true;
                throw new InvalidOperationException(
                    "无法解析 MFA 内部类型 Microsoft.Agents.AI.FileEditor;MFA 版本可能已变更。");
            }

            s_applyReplace = type.GetMethod("ApplyReplace", BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(string), typeof(string), typeof(string), typeof(bool)]);
            s_applyReplaceLines = type.GetMethod("ApplyReplaceLines", BindingFlags.Static | BindingFlags.NonPublic,
                [typeof(string), typeof(IReadOnlyList<FileLineEdit>)]);

            if (s_applyReplace is null || s_applyReplaceLines is null)
            {
                s_resolved = true;
                throw new InvalidOperationException(
                    "MFA 内部 FileEditor 的方法签名已变更,无法反射调用 replace 逻辑。");
            }

            s_type = type;
            s_resolved = true;
        }
    }

    public static (string Content, int Count) ApplyReplace(string content, string oldString, string newString, bool replaceAll)
    {
        try
        {
            EnsureResolved();
            object? result = s_applyReplace!.Invoke(null, [content, oldString, newString, replaceAll]);
            if (result is null) throw new InvalidOperationException("FileEditor.ApplyReplace 返回 null。");
            // 返回值为 (string Content, int Count) 值元组
            (string contentOut, int count) = ((string Content, int Count))result;
            return (contentOut, count);
        }
        catch (TargetInvocationException ex)
        {
            // 还原 MFA 内部抛出的异常(如 old_string 未找到/重复)
            throw ex.InnerException!;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Log.Error($"反射调用 MFA FileEditor.ApplyReplace 失败: {ex}");
            throw;
        }
    }

    public static string ApplyReplaceLines(string content, IReadOnlyList<FileLineEdit> edits)
    {
        try
        {
            EnsureResolved();
            object? result = s_applyReplaceLines!.Invoke(null, [content, edits]);
            if (result is null) throw new InvalidOperationException("FileEditor.ApplyReplaceLines 返回 null。");
            return (string)result;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Log.Error($"反射调用 MFA FileEditor.ApplyReplaceLines 失败: {ex}");
            throw;
        }
    }
}
