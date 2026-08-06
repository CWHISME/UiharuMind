/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
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
    /// 智能体的工作循环段：先弄清事实、边做边说、失败换路、收尾总结。弱模型最依赖这几条。
    ///
    /// 这段<b>不进 harness 层</b>，而是作为角色提示词的一部分（内置智能体的存档里就写着它，
    /// 新建智能体档角色时预填这一段，片段库里也有一份可随时插回）。
    /// 曾经的做法是把框架的 <see cref="HarnessAgent.DefaultInstructions"/> 拼在 harness 段开头，
    /// 那样有两个毛病：用户在界面上看不到每轮都发出去的这段话；而且框架那段的第一句是
    /// "You are a helpful AI assistant..."，harness 段又排在角色段之前，于是每个智能体的人格
    /// 都要先跟一句"你是通用助手"抢身份。见 ADR 0004。
    ///
    /// 标题用一级：与角色卡里的 Task、Style 两节同级——它现在是角色提示词的一节。
    /// </summary>
    public const string AgentWorkLoop =
        "# Work loop\n" +
        "- Establish the facts before acting. Break complex work into explicit steps.\n" +
        "- Between tool calls, say what you learned and what you will do next. " +
        "Never fire more than 4 tool calls in a row without saying anything.\n" +
        "- If a call fails or returns something unexpected, change approach instead of repeating it.\n" +
        "- Finish with a short summary of what you did and what you found.";

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

    /// <summary>知识库检索工具纪律段默认正文</summary>
    public const string KnowledgeSearchDefault =
        "- To look something up in the documents the user attached to this session, call `" +
        KnowledgeTool.ToolName + "` with a short focused query — it is an embedding search, " +
        "so keywords beat whole sentences.\n" +
        "- It returns passages, or reports that no knowledge base is attached. " +
        "If nothing is attached, say so instead of guessing.";

    // 这里刻意没有"文件记忆纪律段":框架的 FileMemoryProvider 自己就会注入一整段
    // ## File Based Memory(先 ls/grep 查已有记忆、用描述性文件名、写入时附描述、
    // 大块数据落盘以免被压缩截断),我们再写一段只会让同样的话在系统提示里出现两遍。
    // 要改那段措辞只能自建 provider —— HarnessAgentOptions 不暴露 FileMemoryProviderOptions。

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

    /// <summary>知识库检索工具纪律段(覆盖优先,空则默认)</summary>
    public static string ResolveKnowledgeSearch(AgentSettingConfig config)
    {
        return Resolve(config.KnowledgeSearchPrompt, KnowledgeSearchDefault);
    }

    /// <summary>
    /// 两种"记忆"都启用时的辨析句。<b>不可被设置页覆盖</b>，也只在两者都挂载时出现——
    /// 模型这一侧同时看得见 <c>file_memory_*</c> 与 <c>knowledge_search</c>，
    /// 名字都像"搜记忆"，而选错的表现是检索不到却不报错。
    /// </summary>
    public static readonly string MemoryDisambiguation =
        $"Two different stores: `{FileMemoryProvider.GrepToolName}` searches notes you wrote yourself, " +
        $"`{KnowledgeTool.ToolName}` searches documents the user attached. Never substitute one for the other.";

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
