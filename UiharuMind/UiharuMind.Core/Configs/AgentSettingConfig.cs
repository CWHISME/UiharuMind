/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Configs;

/// <summary>
/// Agent 工作区标量配置
/// </summary>
public class AgentSettingConfig : TConfigBase<AgentSettingConfig>
{
    /// <summary>新会话默认权限档(0 只读 / 1 自动编辑 / 2 完全自动)</summary>
    public int DefaultPermissionModeIndex { get; set; } = 1;

    /// <summary>新会话默认工作目录(空 = 不绑定)</summary>
    public string DefaultWorkspacePath { get; set; } = string.Empty;

    /// <summary>新会话默认开启 plan 模式</summary>
    public bool DefaultPlanMode { get; set; }

    /// <summary>禁用的技能名列表(SKILL.md 技能目录名)</summary>
    public List<string> DisabledSkills { get; set; } = new();

    /// <summary>启用文件操作工具(Glob/Read/Write/Edit/Replace/Delete/Grep)</summary>
    public bool EnableFileAccess { get; set; } = true;

    /// <summary>启用 Shell 执行工具</summary>
    public bool EnableShellExecution { get; set; } = true;

    /// <summary>启用网络搜索工具(web_search/web_fetch)</summary>
    public bool EnableWebSearch { get; set; } = true;

    /// <summary>启用 agent 笔记(框架文件记忆,agent 跨会话自记;与会话挂载的嵌入知识库是两回事)</summary>
    public bool EnableAgentNotes { get; set; } = true;

    /// <summary>启用定时任务工具(create_scheduled_task)</summary>
    public bool EnableScheduledTasks { get; set; } = true;

    /// <summary>启用识图工具(ask_vision,委托视觉模型答图片问题)</summary>
    public bool EnableVisionTool { get; set; } = true;

    /// <summary>启用记忆检索工具(memory_search,检索会话挂载的嵌入知识库)</summary>
    public bool EnableMemorySearchTool { get; set; } = false;

    /// <summary>启用子代理工具(run_subagent,把只读探查委派给子代理,过程不吃主上下文)</summary>
    public bool EnableSubAgent { get; set; } = true;

    /// <summary>启用任务清单(框架 TodoProvider;关闭时对话侧栏的任务清单同步隐藏)</summary>
    public bool EnableTodoList { get; set; } = false;

    /// <summary>启用计划模式(框架 AgentModeProvider 的 plan/execute;关闭时输入框的模式切换同步隐藏)</summary>
    public bool EnableAgentMode { get; set; } = true;

    /// <summary>Tavily 搜索 API key(填入后搜索优先走正规 API,空则用爬页面兜底链)</summary>
    public string TavilyApiKey { get; set; } = string.Empty;

    /// <summary>Brave Search API key(同上,优先级次于 Tavily)</summary>
    public string BraveSearchApiKey { get; set; } = string.Empty;

    /// <summary>文件工具纪律段的自定义提示词(空 = 用内置默认,见 AgentToolPrompts)</summary>
    public string FileAccessPrompt { get; set; } = string.Empty;

    /// <summary>识图工具纪律段的自定义提示词(空 = 用内置默认)</summary>
    public string VisionToolPrompt { get; set; } = string.Empty;

    /// <summary>记忆检索工具纪律段的自定义提示词(空 = 用内置默认)</summary>
    public string MemorySearchPrompt { get; set; } = string.Empty;

    /// <summary>子代理工具纪律段的自定义提示词(空 = 用内置默认)</summary>
    public string SubAgentPrompt { get; set; } = string.Empty;
}
