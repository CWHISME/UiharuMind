using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// <see cref="AgentAssemblyFacts.Capture(CharacterData, AgentAssemblyInputs)"/> 除角色外的全部入参。
/// 都是已经取好的值，不碰单例，所以可单测；缺省值就是「没有 / 未开」。
/// </summary>
public sealed record AgentAssemblyInputs
{
    /// <summary>重算好的系统提示词</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>工作目录</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>权限档</summary>
    public EAgentPermissionMode Permission { get; init; }

    /// <summary>shell 预授权模式</summary>
    public IReadOnlyList<string>? PreAuthorizedShellPatterns { get; init; }

    /// <summary>MCP 工具集修订号</summary>
    public int McpRevision { get; init; }

    /// <summary>工作区说明文件内容</summary>
    public string WorkspaceInstructions { get; init; } = string.Empty;

    /// <summary>当前模型是否自带视觉</summary>
    public bool ModelSupportsVision { get; init; }

    /// <summary>已解析的子智能体名单（过滤规则见 <c>CharacterRunnerFactory.ResolveMountedAgents</c>）</summary>
    public IReadOnlyList<CharacterData>? MountedAgents { get; init; }

    /// <summary>受管 Python 环境是否已就绪</summary>
    public bool PythonEnvReady { get; init; }

    /// <summary>产出目录名</summary>
    public string OutputFolderName { get; init; } = string.Empty;

    /// <summary>子会话身份指纹；主会话为空串</summary>
    public string SubAgentKey { get; init; } = string.Empty;

    /// <summary>技能清单是否发给模型（全局开关）</summary>
    public bool ModelSkillsEnabled { get; init; } = true;

    /// <summary>此刻可用的内置技能名（换行拼接）：清单关着时写进 Skill 工具描述，开发者模式一开关就变</summary>
    public string BuiltInSkills { get; init; } = string.Empty;

    /// <summary>会话形态；null = 跟角色身份（ADR 0050）</summary>
    public bool? IsAgentForm { get; init; }

    /// <summary>群场景段正文；不是群成员为空串</summary>
    public string GroupScene { get; init; } = string.Empty;

    /// <summary>按需 MCP 名单签名（<see cref="McpManager.DescribeOnDemand"/>）</summary>
    public string McpOnDemand { get; init; } = string.Empty;

    /// <summary>至少配了一个生图模型</summary>
    public bool ImageModelsConfigured { get; init; }
}
