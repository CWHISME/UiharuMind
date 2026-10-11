/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 一次扩展调用要不要问用户（ADR 0079）
/// </summary>
public enum EExtensionRisk
{
    /// <summary>只读：三档都不问</summary>
    ReadOnly,

    /// <summary>一般：三档都问，本会话放行过这个扩展就不问</summary>
    Standard,

    /// <summary>硬问：永远问，本会话放行也不算（接管、浏览器去本机或内网地址）</summary>
    AlwaysAsk,
}

/// <summary>
/// 扩展调用的风险分级。<b>只有这一处定义</b>：审批规则、离席化身、子代理的嵌套审批、审批卡都问它
/// </summary>
public static class ExtensionRisk
{
    /// <summary>
    /// 分级
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <param name="mcpReadOnly">MCP 工具是否自己声明了只读（server 名, 工具名）；null 时一律按「不是」</param>
    /// <returns>风险；不是扩展调用为 null</returns>
    public static EExtensionRisk? Classify(FunctionCallContent call, Func<string, string, bool>? mcpReadOnly = null)
    {
        if (!ExtensionCalls.IsCall(call)) return null;
        // 拆不开（缺扩展名或命令名、参数 JSON 坏了）的调用到了工具里只会报错，什么也做不了：
        // 当只读放过去，让模型立刻拿到报错重发——拦下来只会弹一张空卡（与越界写入缺路径同一口径）
        if (!ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation)) return EExtensionRisk.ReadOnly;

        // 内置扩展没有这条命令：执行那边认的是同一个解析（见 ExtensionHub），什么也不会跑，不为它弹卡
        if (BuiltinExtensions.Canonical(invocation.Extension) is { } builtin &&
            BuiltinExtensions.ToolNameOf(builtin, invocation.Command) == null) return EExtensionRisk.ReadOnly;

        if (invocation.Extension == BuiltinExtensions.Browser)
        {
            if (invocation.Command == BuiltinExtensions.BrowserSnapshot) return EExtensionRisk.ReadOnly;
            return BrowserRisk.RequiresApprovalInAllModes(ExtensionCalls.Unwrap(call))
                ? EExtensionRisk.AlwaysAsk
                : EExtensionRisk.Standard;
        }

        if (invocation.Extension == BuiltinExtensions.Desktop)
        {
            FunctionCallContent inner = ExtensionCalls.Unwrap(call);
            if (DesktopRisk.IsReadOnly(inner)) return EExtensionRisk.ReadOnly;
            return DesktopRisk.RequiresApprovalInAllModes(inner) ? EExtensionRisk.AlwaysAsk : EExtensionRisk.Standard;
        }

        if (BuiltinExtensions.Canonical(invocation.Extension) != null) return EExtensionRisk.Standard;

        return mcpReadOnly?.Invoke(invocation.Extension, invocation.Command) == true
            ? EExtensionRisk.ReadOnly
            : EExtensionRisk.Standard;
    }

    /// <summary>
    /// 是不是硬问的那一类（离席化身直接拒、子代理不自动批、审批卡不给「本会话允许」）
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <returns>是为 true</returns>
    public static bool AlwaysAsks(FunctionCallContent? call) =>
        call != null && Classify(call) == EExtensionRisk.AlwaysAsk;

    /// <summary>
    /// 「本会话允许」记在哪一格：多数扩展整个记；桌面按应用记（<c>desktop:com.apple.TextEdit</c>），
    /// 放行记事本不连带放行微信
    /// </summary>
    /// <param name="invocation">拆开的扩展调用</param>
    /// <param name="call">原调用</param>
    /// <param name="desktop">桌面宿主（按窗口编号认应用）；缺省用全应用那一个</param>
    /// <returns>放行名单里的一格</returns>
    public static string ApprovalKey(ExtensionInvocation invocation, FunctionCallContent call, DesktopHost? desktop = null) =>
        invocation.Extension == BuiltinExtensions.Desktop
            ? $"{BuiltinExtensions.Desktop}:{DesktopRisk.ApprovalScope(ExtensionCalls.Unwrap(call), desktop ?? DesktopHost.Shared)}"
            : invocation.Extension;

    /// <summary>
    /// 本会话放行名单是否覆盖了这一格
    /// </summary>
    /// <param name="approved">会话放行名单（<see cref="ExtensionCalls.AllExtensions"/> 代表全部）</param>
    /// <param name="key">放行的格子（见 <see cref="ApprovalKey"/>；多数扩展就是扩展名）</param>
    /// <returns>覆盖为 true</returns>
    public static bool IsApproved(IReadOnlyList<string>? approved, string key) =>
        approved is { Count: > 0 } && approved.Any(x =>
            x == ExtensionCalls.AllExtensions || string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
}
