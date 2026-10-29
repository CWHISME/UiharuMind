/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.AI.Execution.Tools.Mcp;

/// <summary>
/// <c>mcp</c> 扩展背后的两条命令：<c>list</c> 看配了什么，<c>connect</c> 现在就连。
///
/// 它们不新增任何能力——查、调某个 server 本来也会懒连（<see cref="McpBridge"/>）。
/// 补的是<b>看得见</b>：没有可用 server 时模型从名单上根本不知道 MCP 这回事，
/// 也就不会想到「用户那台可能只是还没起来」。
/// </summary>
internal static class McpAdminTools
{
    /// <summary>list：全部已配置的 server 与各自的状态</summary>
    public const string ListToolName = "McpList";

    /// <summary>connect：现在就连接一台</summary>
    public const string ConnectToolName = "McpConnect";

    /// <summary>
    /// 造两条命令
    /// </summary>
    /// <param name="bridge">本次装配的 MCP 桥接</param>
    /// <returns>list / connect</returns>
    public static IReadOnlyList<AITool> Create(McpBridge bridge) =>
    [
        ToolJson.CreateFunction(
            () => List(bridge),
            ListToolName,
            "列出用户配置的全部 MCP server（含没开托管、待授权、被本智能体禁用、此刻离线的那几台），" +
            "每台一句话说明它现在能不能用、被什么挡着。一台都没配时如实说明。" +
            "要连某台、或看它有哪些命令，用扩展名去查（每个 server 自己也是一个扩展）。"),
        ToolJson.CreateFunction(
            async ([Description("server 名，与 mcp.list 显示的完全一致")]
                    string server,
                CancellationToken cancellationToken = default) =>
                await bridge.OpenAsync(server, cancellationToken).ConfigureAwait(false),
            ConnectToolName,
            "现在就连接一台 MCP server 并取回它的命令清单（不等后台重连退避）。" +
            "离线的那台可能过一会儿才起来，连不上就用它再试一次，别以为它没了；" +
            "没开托管或未经用户授权的连不上，照实告诉用户去哪儿开、去哪儿确认，不要自己拿 shell 或 HTTP 去连。"),
    ];

    private static string List(McpBridge bridge)
    {
        IReadOnlyList<McpServerBrief> all = bridge.Overview();
        if (all.Count == 0)
        {
            return "用户还没配置任何 MCP server，所以现在没有可连的外部应用。" +
                   "要接的话在应用的 设置 → MCP 里加一台，或在当前项目目录配置 *.mcp.json，加完用 mcp.list 复查。";
        }

        StringBuilder sb = new($"MCP server（{all.Count} 台；每台自己也是一个扩展，用 {ExtensionCalls.HelpName} 传它的名字可看命令）：\n");
        foreach (McpServerBrief server in all)
        {
            string name = ExtensionCalls.ModelName(server.Name);
            sb.Append("- ").Append(name).Append("：").Append(McpHelpFormatter.DescribeServer(server));
            if (!string.IsNullOrWhiteSpace(server.Description)) sb.Append("；").Append(server.Description.Trim());
            sb.Append('\n');
        }

        sb.Append($"要连一台就用 {BuiltinExtensions.Mention(BuiltinExtensions.Mcp, BuiltinExtensions.McpConnect)}" +
                  "（参数 server 填名字），要调它的命令就先查它。");
        return sb.ToString();
    }
}
