/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 扩展给人看的名字：内置扩展按界面语言叫「浏览器 / 桌面 / 生图 / 音频 / 工作树」，MCP server 就用它自己的名字。
/// 模型那边认的始终是 browser 这样的标识，只有界面换。
/// 本地化 key 走编译期引用（LangKey 生成器按源码扫描），加扩展时在这里补两行
/// </summary>
public static class ExtensionLabels
{
    /// <summary>
    /// 显示名
    /// </summary>
    /// <param name="extension">扩展名</param>
    /// <returns>内置扩展的本地化名字；其余原样</returns>
    public static string Of(string extension) => BuiltinExtensions.Canonical(extension) switch
    {
        BuiltinExtensions.Browser => Loc.Text(LangKey.ExtensionNameBrowser),
        BuiltinExtensions.Desktop => Loc.Text(LangKey.ExtensionNameDesktop),
        BuiltinExtensions.Image => Loc.Text(LangKey.ExtensionNameImage),
        BuiltinExtensions.Audio => Loc.Text(LangKey.ExtensionNameAudio),
        BuiltinExtensions.Worktree => Loc.Text(LangKey.ExtensionNameWorktree),
        _ => extension,
    };

    /// <summary>
    /// 一句话说明（能力面板勾选项的说明）
    /// </summary>
    /// <param name="extension">扩展名</param>
    /// <returns>本地化说明；内置扩展没配说明或不是内置为空串</returns>
    public static string DescriptionOf(string extension) => BuiltinExtensions.Canonical(extension) switch
    {
        BuiltinExtensions.Browser => Loc.Text(LangKey.ExtensionBrowserDesc),
        BuiltinExtensions.Desktop => Loc.Text(LangKey.ExtensionDesktopDesc),
        BuiltinExtensions.Image => Loc.Text(LangKey.ExtensionImageDesc),
        BuiltinExtensions.Audio => Loc.Text(LangKey.ExtensionAudioDesc),
        BuiltinExtensions.Worktree => Loc.Text(LangKey.ExtensionWorktreeDesc),
        _ => string.Empty,
    };
}
