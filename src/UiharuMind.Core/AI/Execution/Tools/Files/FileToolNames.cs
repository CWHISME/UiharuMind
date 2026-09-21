/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 五个文件工具的名字。
///
/// 单独一个 public 类而不是挂在 <c>PermissiveFileAccessTools</c> 上：那个类是 internal（工具实现
/// 不该外流出 Core），而<b>名字</b>界面层要用——工具卡片的图标、审批卡片按工具名分派 diff。
/// 挂在 internal 类上的结果是界面层只能写字面量 <c>"Edit"</c>，改名时静默失配：
/// <c>GetToolIcon</c> 至今还在认 <c>file_access_</c> 前缀，而那批工具早就不叫这个名了，
/// 症状是文件工具的卡片显示成通用扳手图标。
/// </summary>
public static class FileToolNames
{
    /// <summary>读文件</summary>
    public const string Read = "Read";

    /// <summary>新建或整份替换文件</summary>
    public const string Write = "Write";

    /// <summary>按唯一匹配改文件</summary>
    public const string Edit = "Edit";

    /// <summary>按 glob 找文件</summary>
    public const string Glob = "Glob";

    /// <summary>搜文件内容</summary>
    public const string Grep = "Grep";

    /// <summary>会改动文件的那几个。越界写入的审批判据按这份名单认工具</summary>
    public static readonly string[] Mutating = [Write, Edit];

    /// <summary>全部五个（界面按它给文件类工具配图标）</summary>
    public static readonly string[] All = [Read, Write, Edit, Glob, Grep];
}
