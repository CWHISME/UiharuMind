/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 桌面调用的风险（ADR 0084）：列窗口、拍快照只读；动手、打开一般，「本会话允许」只对当时那个应用有效；
/// 真实键鼠与接管硬问——前者抢用户的输入，后者等的是用户的手。
/// 往密码框打字不在这里：工具当场拒（见 <see cref="DesktopHost"/>），点过「本会话允许」也拦得住
/// </summary>
internal static class DesktopRisk
{
    /// <summary>
    /// 只读的（三档都不问）
    /// </summary>
    /// <param name="call">拆开后的调用</param>
    /// <returns>是为 true</returns>
    public static bool IsReadOnly(FunctionCallContent call) =>
        call.Name is DesktopTools.WindowsToolName or DesktopTools.SnapshotToolName;

    /// <summary>
    /// 任何权限档都要用户点一次的
    /// </summary>
    /// <param name="call">拆开后的调用</param>
    /// <returns>是为 true</returns>
    public static bool RequiresApprovalInAllModes(FunctionCallContent call) => call.Name switch
    {
        DesktopTools.HandoffToolName => true,
        DesktopTools.ActToolName => DesktopTools.IsReal(Text(call.Arguments, "input")),
        _ => false,
    };

    /// <summary>
    /// 「本会话允许」记在哪一格：动手按应用（放行记事本不连带放行微信），打开按打开的东西
    /// </summary>
    /// <param name="call">拆开后的调用</param>
    /// <param name="host">桌面宿主：按窗口编号认应用，不碰系统</param>
    /// <returns>格子的名字；认不出应用时用模型写的窗口</returns>
    public static string ApprovalScope(FunctionCallContent call, DesktopHost host)
    {
        if (call.Name == DesktopTools.OpenToolName) return "open " + (Text(call.Arguments, "target") ?? string.Empty).Trim();
        string window = (Text(call.Arguments, "window") ?? string.Empty).Trim();
        return host.FindWindow(window) is { } found ? found.Info.AppId : window;
    }

    private static string? Text(IDictionary<string, object?>? arguments, string name)
    {
        if (arguments == null || !arguments.TryGetValue(name, out object? value)) return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }
}
