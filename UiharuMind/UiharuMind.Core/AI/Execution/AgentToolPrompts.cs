/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 工具纪律段的默认提示词与解析。默认文本收在这里的目的：
/// 设置页要展示默认值、允许覆盖并可一键重置——配置里只存"覆盖"(空 = 用默认)，
/// 默认措辞升级时未覆盖的用户自动跟随。段落标题由装配侧统一加，这里只管正文。
/// </summary>
public static class AgentToolPrompts
{
    /// <summary>
    /// 工作目录段。<b>不可被设置页覆盖</b>——这一段是事实而非建议,
    /// 用户覆盖掉文件工具的纪律文案不该顺带把"根目录在哪"一起删掉。
    ///
    /// 这段曾经不存在:工作目录只被拿去构造工具,从没进过任何提示词。
    /// 后果是模型不知道根在哪,于是自己编一个占位路径(实机见过
    /// <c>Glob(pattern: "*.*", root: "/path/to/project")</c>),白烧一次工具调用。
    /// </summary>
    /// <param name="workingDirectory">文件与 shell 工具的根目录绝对路径</param>
    /// <returns>提示词段落正文</returns>
    public static string BuildWorkingDirectory(string workingDirectory)
    {
        // 路径不加反引号:反引号在提示词里专表工具名,有不变量测试按这条约定校验。
        // 也刻意不表态"该用相对还是绝对":文件工具纪律段与工作区 AGENTS.md 都可能有自己的主张,
        // 这里多一句偏好就会和它们打架(而互相矛盾的路径指示正是模型乱编路径的来源之一)
        return $"Your working directory is \"{workingDirectory}\".\n" +
               "Tool paths may be absolute, or relative to that directory.\n" +
               "Never invent a path and never pass a placeholder such as /path/to/project. " +
               "If you do not know where something is, search for it first.";
    }

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

    /// <summary>子代理工具纪律段默认正文</summary>
    public const string SubAgentDefault =
        "- When a question needs you to read a lot of material before you can answer " +
        "(surveying many files, researching a topic), delegate it with `" + SubAgentTool.ToolName + "` " +
        "instead of reading everything yourself. You get back a report; the raw material never enters this context.\n" +
        "- Say what you want found, over what scope, and in what shape. Vague tasks come back vague.\n" +
        "- It blocks until the sub-agent finishes. The sub-agent cannot ask you anything and nobody " +
        "will approve anything for it, so put everything it needs into the task.";

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

    /// <summary>子代理工具纪律段(覆盖优先,空则默认)</summary>
    public static string ResolveSubAgent(AgentSettingConfig config)
    {
        return Resolve(config.SubAgentPrompt, SubAgentDefault);
    }

    private static string Resolve(string overrideText, string defaultText)
    {
        return string.IsNullOrWhiteSpace(overrideText) ? defaultText : overrideText.Trim();
    }
}
