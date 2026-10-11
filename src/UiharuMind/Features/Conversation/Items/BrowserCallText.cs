/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 浏览器调用在审批卡上的说法：「点击按钮『提交订单』」加所在网页，而不是 <c>{"action":"click","target":"e21"}</c>——
/// 后者没人判断得了该不该批。元素按发起会话最近一份快照翻回（<see cref="BrowserTools.Preview"/>），不碰页面
/// </summary>
internal static class BrowserCallText
{
    private const int MaxValueChars = 80; //填的字很长时只露个头，卡片不是看全文的地方

    /// <summary>密码框的值在卡上只给圆点</summary>
    internal const string SecretMask = "••••••";

    /// <summary>
    /// 说清一次浏览器调用
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <param name="host">浏览器宿主</param>
    /// <param name="sessionId">发起调用的会话；不知道时为 null</param>
    /// <returns>做什么（第二行是所在网站）；不是浏览器的导航、动作、接管，或不知道会话时为 null</returns>
    public static string? Describe(FunctionCallContent call, IBrowserHost host, string? sessionId) =>
        sessionId != null && BrowserTools.Preview(call, host, sessionId) is { } preview ? Describe(preview) : null;

    /// <summary>
    /// 按预览写文案
    /// </summary>
    /// <param name="preview">调用预览</param>
    /// <returns>做什么；所在网站另起一行（导航不带：要去哪已经写在第一行）</returns>
    public static string Describe(BrowserCallPreview preview)
    {
        bool navigate = preview.ToolName == BrowserTools.NavigateToolName;
        string what = navigate ? Loc.Text(LangKey.BrowserCallNavigate, preview.Value)
            : preview.ToolName == BrowserTools.HandoffToolName ? preview.Value
            : DescribeAction(preview);
        string site = navigate ? string.Empty : SiteOf(preview.PageUrl);
        return site.Length > 0 ? what + "\n" + Loc.Text(LangKey.BrowserCallSite, site) : what;
    }

    private static string DescribeAction(BrowserCallPreview preview)
    {
        string element = ElementText(preview.Element, preview.Target);
        string value = preview.Element?.Secret == true ? SecretMask : Shorten(preview.Value);
        return preview.Action switch
        {
            "click" => Loc.Text(LangKey.BrowserCallClick, element),
            "fill" => Loc.Text(LangKey.BrowserCallFill, element, value),
            "select" => Loc.Text(LangKey.BrowserCallSelect, element, value),
            "check" => Loc.Text(LangKey.BrowserCallCheck, element),
            "uncheck" => Loc.Text(LangKey.BrowserCallUncheck, element),
            "press" when preview.Target.Length == 0 => Loc.Text(LangKey.BrowserCallPressPage, value),
            "press" => Loc.Text(LangKey.BrowserCallPress, element, value),
            "hover" => Loc.Text(LangKey.BrowserCallHover, element),
            "upload" => Loc.Text(LangKey.BrowserCallUpload, element, Files(preview.Value)),
            "scroll" => Loc.Text(string.Equals(preview.Value.Trim(), "up", StringComparison.OrdinalIgnoreCase)
                ? LangKey.BrowserCallScrollUp
                : LangKey.BrowserCallScrollDown),
            "back" => Loc.Text(LangKey.BrowserCallBack),
            // 认不出的动作工具自己会拒，这里原样摆出来
            _ => $"{preview.Action} {preview.Target} {value}".Trim(),
        };
    }

    /// <summary>
    /// 元素的说法：「按钮『保存』」；翻不回元素（编号过期、文字没对上）就照模型写的原样摆，不猜。桌面的卡也用这一份
    /// </summary>
    /// <param name="element">翻回的元素；翻不回为 null</param>
    /// <param name="target">模型写的目标原样</param>
    /// <returns>说法</returns>
    internal static string ElementText(BrowserElement? element, string target)
    {
        if (element == null) return Loc.Text(LangKey.BrowserElementRaw, target);

        string role = Loc.Text(RoleKey(element));
        return element.Name.Length > 0
            ? Loc.Text(LangKey.BrowserElementNamed, role, Shorten(element.Name))
            : Loc.Text(LangKey.BrowserElementUnnamed, role);
    }

    private static LangKey RoleKey(BrowserElement element) => element.Role.ToLowerInvariant() switch
    {
        "button" => LangKey.BrowserRoleButton,
        "link" => LangKey.BrowserRoleLink,
        "textbox" => element.Secret ? LangKey.BrowserRolePassword : LangKey.BrowserRoleTextbox,
        "searchbox" => LangKey.BrowserRoleSearchbox,
        "combobox" => LangKey.BrowserRoleCombobox,
        "listbox" => LangKey.BrowserRoleListbox,
        "option" => LangKey.BrowserRoleOption,
        "checkbox" => LangKey.BrowserRoleCheckbox,
        "radio" => LangKey.BrowserRoleRadio,
        "switch" => LangKey.BrowserRoleSwitch,
        "slider" => LangKey.BrowserRoleSlider,
        "spinbutton" => LangKey.BrowserRoleSpinbutton,
        "menuitem" or "menuitemcheckbox" or "menuitemradio" => LangKey.BrowserRoleMenuitem,
        "tab" => LangKey.BrowserRoleTab,
        "treeitem" => LangKey.BrowserRoleTreeitem,
        _ => LangKey.BrowserRoleElement,
    };

    // 文件给全路径不截断：传的是哪个文件正是这张卡要用户判断的
    private static string Files(string value) =>
        string.Join("、", value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // 只给站点：防钓鱼看的就是这一截，完整地址常是一长串百分号编码
    private static string SiteOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0 ? uri.Host : url;

    /// <summary>
    /// 长文字只露个头
    /// </summary>
    /// <param name="text">文字</param>
    /// <returns>压成一行、过长加省略号</returns>
    internal static string Shorten(string text)
    {
        string flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= MaxValueChars ? flat : flat[..MaxValueChars] + "…";
    }
}
