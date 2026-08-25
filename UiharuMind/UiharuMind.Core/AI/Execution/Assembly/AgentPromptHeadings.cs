/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Assembly;

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

    /// <summary>工作目录段的标题正文（不含级别前缀，见 <see cref="WorkingDirectory"/>）</summary>
    public const string WorkingDirectoryName = "工作目录";

    /// <summary>文件操作纪律段</summary>
    public const string FileOperations = "## 文件操作";

    /// <summary>识图纪律段</summary>
    public const string Images = "## 图像";

    /// <summary>知识库检索纪律段</summary>
    public const string KnowledgeBase = "## 知识库";

    /// <summary>命令行纪律段</summary>
    public const string Shell = "## 命令行";

    /// <summary>受管 Python 环境纪律段。它挂在命令行之下的同级位置——Python 由 shell 跑</summary>
    public const string Python = "## Python";

    /// <summary>子代理委派纪律段</summary>
    public const string Delegation = "## 委派";

    /// <summary>MCP server 自述段（主 agent 与子代理共用）</summary>
    public const string Mcp = "# MCP 服务器";

    /// <summary>工作区规矩段（主 agent 与子代理共用）</summary>
    public const string Workspace = "# 工作区规矩（来自项目的 AGENTS.md）";

    /// <summary>子代理的身份段</summary>
    public const string SubAgentRole = "# 角色";

    /// <summary>子代理的做法段</summary>
    public const string SubAgentMethod = "# 做法";

    /// <summary>
    /// 工作目录段的标题。级别<b>随装配形态而变</b>：主 agent 里它是「# 工具」的分项，
    /// 子代理里没有那层外壳，它自己就是顶级段。
    /// </summary>
    /// <param name="headingPrefix">级别前缀（<c>"#"</c> 或 <c>"##"</c>）</param>
    /// <returns>整行标题</returns>
    public static string WorkingDirectory(string headingPrefix) => $"{headingPrefix} {WorkingDirectoryName}";
}
