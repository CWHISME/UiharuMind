/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 桌面调用在审批卡上的说法（ADR 0084）：「点击按钮『保存』」加所在窗口，而不是 <c>{"window":"w3","target":"e5"}</c>。
/// 元素按发起会话最近一份快照翻回（<see cref="DesktopHost.Peek"/>），不碰系统；元素的说法与浏览器共用一份
/// </summary>
internal static class DesktopCallText
{
    /// <summary>
    /// 说清一次桌面调用
    /// </summary>
    /// <param name="call">拆开后的工具调用</param>
    /// <param name="host">桌面宿主</param>
    /// <param name="sessionId">发起调用的会话；不知道时为 null</param>
    /// <returns>做什么（第二行是所在窗口）；不是动作、打开、接管，或不知道会话时为 null</returns>
    public static string? Describe(FunctionCallContent call, DesktopHost host, string? sessionId)
    {
        if (sessionId == null) return null;
        IDictionary<string, object?> arguments = call.Arguments ?? new Dictionary<string, object?>();
        string window = Text(arguments, "window");
        switch (call.Name)
        {
            case DesktopTools.OpenToolName:
                string target = Text(arguments, "target").Trim();
                bool isFile = target.Contains('/') || target.Contains('\\') || target.StartsWith('$');
                return Loc.Text(isFile ? LangKey.DesktopCallOpenFile : LangKey.DesktopCallOpenApp, target);
            case DesktopTools.HandoffToolName:
                return WithWindow(Text(arguments, "task"), host.Peek(sessionId, window, string.Empty));
            case DesktopTools.ActToolName:
                string given = Text(arguments, "target").Trim();
                DesktopElementPreview? preview = host.Peek(sessionId, window, given);
                string what = DescribeAction(Text(arguments, "action"), given, Text(arguments, "value"), preview);
                if (DesktopTools.IsReal(Text(arguments, "input"))) what += "\n" + Loc.Text(LangKey.DesktopCallReal);
                return WithWindow(what, preview);
            default:
                return null;
        }
    }

    private static string DescribeAction(string action, string target, string value, DesktopElementPreview? preview)
    {
        BrowserElement? element = preview is { Role.Length: > 0 } found ? new BrowserElement(found.Role, found.Name, found.Secret) : null;
        string subject = element == null && LooksLikePoint(target)
            ? Loc.Text(LangKey.DesktopElementPoint, target)
            : BrowserCallText.ElementText(element, target);
        string shown = element?.Secret == true ? BrowserCallText.SecretMask : BrowserCallText.Shorten(value);
        return DesktopTools.ParseAction(action) switch
        {
            EDesktopAction.Click => Loc.Text(LangKey.BrowserCallClick, subject),
            EDesktopAction.DoubleClick => Loc.Text(LangKey.DesktopCallDoubleClick, subject),
            EDesktopAction.RightClick => Loc.Text(LangKey.DesktopCallRightClick, subject),
            EDesktopAction.Fill => Loc.Text(LangKey.BrowserCallFill, subject, shown),
            EDesktopAction.Type when target.Length == 0 => Loc.Text(LangKey.DesktopCallTypeFocus, shown),
            EDesktopAction.Type => Loc.Text(LangKey.DesktopCallType, subject, shown),
            EDesktopAction.Select => Loc.Text(LangKey.BrowserCallSelect, subject, shown),
            EDesktopAction.Check => Loc.Text(LangKey.BrowserCallCheck, subject),
            EDesktopAction.Uncheck => Loc.Text(LangKey.BrowserCallUncheck, subject),
            EDesktopAction.Press when target.Length == 0 => Loc.Text(LangKey.BrowserCallPressPage, shown),
            EDesktopAction.Press => Loc.Text(LangKey.BrowserCallPress, subject, shown),
            EDesktopAction.Focus => Loc.Text(LangKey.DesktopCallFocus, subject),
            EDesktopAction.Scroll => Loc.Text(string.Equals(value.Trim(), "up", StringComparison.OrdinalIgnoreCase)
                ? LangKey.BrowserCallScrollUp
                : LangKey.BrowserCallScrollDown),
            // 认不出的动作工具自己会拒，这里原样摆出来
            _ => $"{action} {target} {shown}".Trim(),
        };
    }

    // 所在窗口另起一行：同一个「保存」在哪个应用里点，正是要用户判断的
    private static string WithWindow(string what, DesktopElementPreview? preview) =>
        preview == null ? what : what + "\n" + Loc.Text(LangKey.DesktopCallWindow, preview.App, preview.WindowTitle);

    private static bool LooksLikePoint(string target) =>
        target.Contains(',') && target.Replace(",", string.Empty).Replace(" ", string.Empty).Replace(".", string.Empty)
            .Trim('(', ')').All(char.IsAsciiDigit);

    private static string Text(IDictionary<string, object?> arguments, string key) =>
        arguments.TryGetValue(key, out object? raw)
            ? raw switch
            {
                null => string.Empty,
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
                _ => raw.ToString() ?? string.Empty,
            }
            : string.Empty;
}
