/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 读取工作区根目录的项目级 MCP 配置 <c>.mcp.json</c>，形状与全局那份逐字相同
/// （<see cref="McpServersFile"/>，生态事实标准的 <c>mcpServers</c>）。
///
/// 这不是"通用性"需求：典型的项目级 server（比如 Unity MCP）连的就是某个打开的编辑器，
/// 它天然跟着项目走，放全局配置里语义就是错的。
///
/// <b>只认工作区根一处</b>。Cursor 与 VS Code 各自放在 <c>.cursor/</c>、<c>.vscode/</c> 下，
/// 但一旦允许多来源，覆盖规则就从「项目级 &gt; 全局」变成一串纯属拍脑袋的优先级，用户也记不住。
/// 需要时再加不迟，加它不影响任何已有数据。
///
/// <b>项目级那两项本机状态取默认值</b>（托管、注入自述）：既然一个 server 被写进了项目配置，
/// 就说明这个项目要用它。而它们本就不能写进那个文件——那是要入库共享的，不该带本机状态。
/// 真正拦住"入库即执行"的是安全确认（见 <c>McpTrustStore</c>），不是一个默认关闭的开关。
/// </summary>
internal static class McpWorkspaceConfigLoader
{
    /// <summary>项目级配置的文件名（工作区根目录）</summary>
    public const string FileName = ".mcp.json";

    /// <summary>
    /// 读取某工作区的项目级 server 配置。
    /// </summary>
    /// <param name="workspacePath">工作区根目录；空表示未绑定，返回空列表</param>
    /// <returns>该工作区的配置（每条已带上 <c>WorkspacePath</c>）；无文件或读坏时为空列表</returns>
    public static List<McpServerConfig> Load(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return new List<McpServerConfig>();

        string path = Path.Combine(workspacePath, FileName);
        if (!File.Exists(path)) return new List<McpServerConfig>();

        // 读坏一律按"没有项目级配置"处理,不抛不阻塞:这个文件由仓库作者控制,
        // 一份手写坏的 json 不该让整个会话起不来
        McpServersFile? file = SaveUtility.Load<McpServersFile>(path);
        if (file == null)
        {
            Log.Warning($"Read workspace MCP config '{path}' failed or malformed; treated as absent");
            return new List<McpServerConfig>();
        }

        return file.ToConfigs(EmptyStates, workspacePath);
    }

    /// 项目级没有本机状态文件,一律取 McpServerLocalState 的默认值
    private static readonly Dictionary<string, McpServerLocalState> EmptyStates = new();
}
