/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.Mcp;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// mcp 扩展：MCP server 这一侧的<b>入口</b>——配了哪些、各自被什么挡着、现在就要连哪一台。
///
/// 每个 server 本来各是一个扩展，但它只有<b>能用</b>时才出现在名单上（ADR 0079）：
/// 一台都没配、或配了却全被托管/授权挡着时，模型从名单上完全看不出有 MCP 这回事，
/// 也就不会想到「用户那台可能只是还没起来」。这个扩展常驻，补的正是这一段。
///
/// 两条命令都不新增能力：查、调某个 server 本来就会懒连（<see cref="Mcp.McpBridge"/>），
/// 因此与它们同级、同为只读——真正会启动进程的是用户在设置里打开的托管、和用户点过的授权，
/// 两道闸都在 <see cref="Mcp.McpManager"/> 里，绕不过这里。
/// </summary>
public sealed class McpExtension : BuiltinExtension
{
    /// <summary>规格单例</summary>
    public static readonly McpExtension Spec = new();

    public override string Name => BuiltinExtensions.Mcp;

    public override string Guide =>
        "MCP（Model Context Protocol）server 是用户自己运行的独立应用（本机进程或一条 HTTP 连接），" +
        "接上之后它的工具就成了扩展的命令：查它用 " + ExtensionCalls.HelpName + " 传 server 名，" +
        "调它用 " + ExtensionCalls.CallName + " 传「server 名.命令名」。\n" +
        "- " + BuiltinExtensions.Mention(BuiltinExtensions.Mcp, BuiltinExtensions.McpList) +
        "：列出全部配置好的 server（含没开、待授权、离线），每台说明为什么用不上。先查这里，别猜名字。\n" +
        "- " + BuiltinExtensions.Mention(BuiltinExtensions.Mcp, BuiltinExtensions.McpConnect) +
        "：现在就连接一台并取回它的命令清单。离线的那台可能过一会儿才起来，再 connect 一次就行。\n" +
        "被托管开关或授权挡着的，照实告诉用户去哪儿打开、去哪儿确认；" +
        "不要自己拿 shell 或手工 HTTP 请求去连，那会跳过用户授权。";

    public override string Summary =>
        "管理用户的 MCP server：列出配了哪些（含没开的、待授权的、离线的）、现在就连接某一台";

    public override IReadOnlyList<(string Command, string ToolName)> CommandNames =>
    [
        (BuiltinExtensions.McpList, McpAdminTools.ListToolName),
        (BuiltinExtensions.McpConnect, McpAdminTools.ConnectToolName),
    ];

    /// <summary>两条都只读：list 不碰任何东西，connect 做的正是查一个 server 时本来就会做的事
    /// （建立连接、取回工具清单），两者同一级别</summary>
    public override EExtensionRisk Classify(FunctionCallContent call) => EExtensionRisk.ReadOnly;

    protected override BuiltinExtension CreateAssembled(AgentToolResources resources,
        EReadImageMode imageMode, bool mountFiles)
    {
        if (resources.Mcp.Bridge is not { } bridge)
        {
            McpExtension unavailable = new();
            unavailable.SetUnavailable("这次装配没有接上 MCP 管理器（只会出现在测试或未绑定工作区的场合）。");
            return unavailable;
        }

        McpExtension assembled = new();
        assembled.SetCommands(Bind(McpAdminTools.Create(bridge)));
        return assembled;
    }
}
