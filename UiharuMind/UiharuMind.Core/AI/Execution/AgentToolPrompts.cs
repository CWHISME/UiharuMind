/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 工具纪律段的默认提示词与解析。默认文本收在这里的目的：
/// 设置页要展示默认值、允许覆盖并可一键重置——配置里只存"覆盖"(空 = 用默认)，
/// 默认措辞升级时未覆盖的用户自动跟随。段落标题由装配侧统一加，这里只管正文。
/// </summary>
public static class AgentToolPrompts
{
    /// <summary>文件工具纪律段默认正文</summary>
    public const string FileAccessDefault =
        "- Use `Glob` to find files, `Grep` to search text, and `Read` a file before you change it.\n" +
        "- Always pass explicit paths. If the location is unclear, run one `Glob` first instead of asking.\n" +
        "- Make the smallest edit that works. Never rewrite a whole file for a small change.";

    /// <summary>识图工具纪律段默认正文</summary>
    public const string VisionToolDefault =
        "- Attachments arrive as `[Attached file: <path>]`. To see what an image shows, " +
        "call `ask_vision` with that path. Never guess from the file name.";

    /// <summary>记忆检索工具纪律段默认正文</summary>
    public const string MemorySearchDefault =
        "call `memory_search` with a short focused query by RAG." +
        "It returns snippets, or reports that no library is bound.";

    /// <summary>文件工具纪律段(覆盖优先,空则默认)</summary>
    public static string ResolveFileAccess(AgentSettingConfig config)
    {
        return Resolve(config.FileAccessPrompt, FileAccessDefault);
    }

    /// <summary>识图工具纪律段(覆盖优先,空则默认)</summary>
    public static string ResolveVisionTool(AgentSettingConfig config)
    {
        return Resolve(config.VisionToolPrompt, VisionToolDefault);
    }

    /// <summary>记忆检索工具纪律段(覆盖优先,空则默认)</summary>
    public static string ResolveMemorySearch(AgentSettingConfig config)
    {
        return Resolve(config.MemorySearchPrompt, MemorySearchDefault);
    }

    private static string Resolve(string overrideText, string defaultText)
    {
        return string.IsNullOrWhiteSpace(overrideText) ? defaultText : overrideText.Trim();
    }
}
