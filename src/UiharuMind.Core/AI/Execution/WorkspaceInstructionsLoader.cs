/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 读取工作区根目录的项目说明文件(AGENTS.md,兼容 CLAUDE.md),注入 agent 系统提示。
/// 行业惯例:项目自己的构建方式、规范与禁忌写在这里,agent 到哪个工作区就守哪家的规矩。
/// 内容进入装配快照——文件编辑后下一次挂接自动重建生效。
/// </summary>
internal static class WorkspaceInstructionsLoader
{
    private const int MaxChars = 500; //说明文件也占系统提示预算,超长截断

    private static readonly string[] FileNames = ["AGENTS.md", "CLAUDE.md"];

    /// <summary>
    /// 解析工作区根目录下实际存在的说明文件名（AGENTS.md 优先于 CLAUDE.md）。
    /// 只做存在性判断、不读内容——指针段点名用，比 <see cref="Load"/> 便宜，
    /// 发消息路径（每轮重拼提示词）上可放心调。
    /// </summary>
    /// <param name="workspacePath">工作区根目录;空表示未绑定</param>
    /// <returns>存在的说明文件名;无则空串</returns>
    public static string ResolveFileName(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return string.Empty;

        foreach (string name in FileNames)
        {
            if (File.Exists(Path.Combine(workspacePath, name))) return name;
        }

        return string.Empty;
    }

    /// <summary>
    /// 加载工作区说明。按优先级取第一个存在的文件,超长截断。
    /// </summary>
    /// <param name="workspacePath">工作区根目录;空表示未绑定</param>
    /// <returns>说明文本;无文件或读取失败为空串</returns>
    public static string Load(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return string.Empty;

        string fileName = ResolveFileName(workspacePath);
        if (fileName.Length == 0) return string.Empty;

        string path = Path.Combine(workspacePath, fileName);
        try
        {
            string text = File.ReadAllText(path).Trim();
            if (text.Length > MaxChars)
            {
                text = $"{text[..MaxChars]}\n…[workspace instructions truncated]";
            }

            return text;
        }
        catch (Exception e)
        {
            Log.Warning($"Read workspace instructions '{path}' failed: {e.Message}");
            return string.Empty;
        }
    }
}
