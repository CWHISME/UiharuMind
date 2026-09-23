/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Prompts;

/// <summary>
/// 系统提示里各段落的标题，<b>唯一出处</b>。
///
/// 单独立一个类而不是散在装配侧，是因为不变量测试要靠标题的先后位置校验段落层级
/// （工具纪律必须整体落在「# 工具」之下，等等）。标题字面值一旦在测试里抄一份，
/// 改标题就得满仓库找，而漏改的表现是不变量静默失效——测试还绿着，层级已经错了。
///
/// 标题与正文同为中文（见 ADR 0017）：正文中文、标题英文的话，
/// 每一节开头都要切一次语言，而语言混排正是这次要消掉的东西。
/// </summary>
public static class AgentPromptHeadings
{
    /// <summary>工具纪律段的父标题。角色段的「# 工作循环」与它同级</summary>
    public const string Tools = "# 工具";

    /// <summary>基座层的标题（与角色段「# 工作循环」同级，恒在它之前）</summary>
    public const string Base = "# 基座";

    /// <summary>工作目录段的标题正文（不含级别前缀，见 <see cref="WorkingDirectory"/>）</summary>
    public const string WorkingDirectoryName = "工作目录";

    /// <summary>草稿目录段的标题正文（不含级别前缀，见 <see cref="OutputRoom"/>）</summary>
    public const string OutputRoomName = "草稿目录";

    /// <summary>记忆目录段的标题正文（不含级别前缀，见 <see cref="Memory"/>）</summary>
    public const string MemoryName = "记忆";

    /// <summary>文件读取纪律段</summary>
    public const string FileOperations = "## 文件操作";

    /// <summary>
    /// 文件修改纪律段。与 <see cref="FileOperations"/> 分开，是因为只读装配（探索档子代理、
    /// 关掉写工具的主代理）拿不到 `Edit`/`Write`，而读那几条对它同样成立——
    /// 从前两者并成一段，结果只读侧要么整段丢失（子代理连上下文卫生都没有），
    /// 要么整段发出去（指名了不存在的工具）。
    /// </summary>
    public const string FileModifications = "## 文件修改";

    /// <summary>识图纪律段</summary>
    public const string Images = "## 图像";

    /// <summary>知识库检索纪律段</summary>
    public const string KnowledgeBase = "## 知识库";

    /// <summary>
    /// 联网纪律段。<b>主代理从前没有这一段</b>：它按 <c>EnableWebSearch</c> 挂了
    /// WebSearch/WebFetch 两个工具却零指示，而子代理侧反倒有一句。
    /// 段落清单两档共用之后，这个不对称自然消掉了。
    /// </summary>
    public const string WebAccess = "## 联网";

    /// <summary>命令行纪律段</summary>
    public const string Shell = "## 命令行";

    /// <summary>受管 Python 环境纪律段。它挂在命令行之下的同级位置——Python 由 shell 跑</summary>
    public const string Python = "## Python";

    /// <summary>委派纪律段</summary>
    public const string Delegation = "## 委派";

    /// <summary>MCP server 自述段（主代理与子代理共用）</summary>
    public const string Mcp = "# MCP 服务器";

    /// <summary>工作区规矩段（主代理与子代理共用）</summary>
    public const string Workspace = "# 工作区规矩（来自项目的 AGENTS.md）";

    /// <summary>子代理的身份段</summary>
    public const string SubAgentRole = "# 角色";

    /// <summary>
    /// 子代理的协作段。取代从前的「# 做法」：那一段把工具纪律与协作口径混成一摊裸 bullet，
    /// 工具那部分现已归入 <see cref="Tools"/> 之下的各分节，剩下的才是这一段。
    /// </summary>
    public const string SubAgentCollaboration = "# 协作";

    /// <summary>
    /// 工作目录段的标题。级别<b>随装配形态而变</b>：主代理里它是「# 工具」的分项，
    /// 子代理里没有那层外壳，它自己就是顶级段。
    /// </summary>
    /// <param name="headingPrefix">级别前缀（<c>"#"</c> 或 <c>"##"</c>）</param>
    /// <returns>整行标题</returns>
    public static string WorkingDirectory(string headingPrefix) => $"{headingPrefix} {WorkingDirectoryName}";

    /// <summary>
    /// 草稿目录段的标题。级别随装配形态而变，与 <see cref="WorkingDirectory"/> 同一道理
    /// </summary>
    /// <param name="headingPrefix">级别前缀（<c>"#"</c> 或 <c>"##"</c>）</param>
    /// <returns>整行标题</returns>
    public static string OutputRoom(string headingPrefix) => $"{headingPrefix} {OutputRoomName}";

    /// <summary>
    /// 记忆目录段的标题。级别随装配形态而变，与 <see cref="WorkingDirectory"/> 同一道理
    /// </summary>
    /// <param name="headingPrefix">级别前缀（<c>"#"</c> 或 <c>"##"</c>）</param>
    /// <returns>整行标题</returns>
    public static string Memory(string headingPrefix) => $"{headingPrefix} {MemoryName}";
}
