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
    private const int MaxChars = 16_000; //说明文件也占系统提示预算,超长截断

    private static readonly string[] FileNames = ["AGENTS.md", "CLAUDE.md"];

    /// <summary>
    /// 加载工作区说明。按优先级取第一个存在的文件,超长截断。
    /// </summary>
    /// <param name="workspacePath">工作区根目录;空表示未绑定</param>
    /// <returns>说明文本;无文件或读取失败为空串</returns>
    public static string Load(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return string.Empty;

        foreach (string name in FileNames)
        {
            string path = Path.Combine(workspacePath, name);
            if (!File.Exists(path)) continue;

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

        return string.Empty;
    }
}
