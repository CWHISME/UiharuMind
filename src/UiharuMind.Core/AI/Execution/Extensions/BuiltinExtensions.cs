/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Tools.Browser;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Core.AI.ImageGeneration;

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 一条扩展命令：背后就是原来那把工具，描述与参数 schema 原样拿来渲染帮助、绑定参数
/// </summary>
/// <param name="Name">命令名</param>
/// <param name="Function">执行它的工具</param>
internal sealed record ExtensionCommand(string Name, AIFunction Function);

/// <summary>
/// 一个内置扩展
/// </summary>
/// <param name="Name">扩展名</param>
/// <param name="Guide">用法说明（帮助的开头，相当于 MCP 的 server 自述）</param>
/// <param name="Commands">命令</param>
internal sealed record BuiltinExtension(string Name, string Guide, IReadOnlyList<ExtensionCommand> Commands)
{
    /// <summary>缺前提时的原因：照样列出，但调不了；能用为 null</summary>
    public string? Unavailable { get; init; }
}

/// <summary>
/// 内置扩展（ADR 0079）：名字、命令、各自背后的工具，以及给系统提示与帮助的一句话说明。
/// 命令名与原工具名的对照只在这里，界面据此把 <see cref="ExtensionCalls.CallName"/> 拆回原来那张卡
/// </summary>
public static class BuiltinExtensions
{
    /// <summary>浏览器</summary>
    public const string Browser = "browser";

    /// <summary>生图</summary>
    public const string Image = "image";

    /// <summary>音频：配音成文件、转写</summary>
    public const string Audio = "audio";

    /// <summary>桌面：看、动别的应用的窗口（ADR 0084）</summary>
    public const string Desktop = "desktop";

    /// <summary>浏览器：重读当前页</summary>
    public const string BrowserSnapshot = "snapshot";

    /// <summary>浏览器：打开地址</summary>
    public const string BrowserNavigate = "navigate";

    /// <summary>浏览器：在页面上动手</summary>
    public const string BrowserAct = "act";

    /// <summary>浏览器：交给用户</summary>
    public const string BrowserHandoff = "handoff";

    /// <summary>桌面：列窗口</summary>
    public const string DesktopWindows = "windows";

    /// <summary>桌面：给窗口拍快照</summary>
    public const string DesktopSnapshot = "snapshot";

    /// <summary>桌面：在窗口里动手</summary>
    public const string DesktopAct = "act";

    /// <summary>桌面：启动应用、打开文件</summary>
    public const string DesktopOpen = "open";

    /// <summary>桌面：交给用户</summary>
    public const string DesktopHandoff = "handoff";

    /// <summary>生图：生成或修改</summary>
    public const string ImageGenerate = "generate";

    /// <summary>音频：配音成文件</summary>
    public const string AudioSynthesize = "synthesize";

    /// <summary>音频：转写</summary>
    public const string AudioTranscribe = "transcribe";

    /// <summary>一次调用的完整写法，报错与工具描述共用</summary>
    public const string CallExample =
        """{"extension": "browser", "command": "act", "arguments": {"action": "fill", "target": "e3", "value": "hello"}}""";

    /// <summary>
    /// 内置命令的速查，写进 <see cref="ExtensionCalls.CallName"/> 的描述：模型不必先查三轮帮助就能直接调对。
    /// 是常量（缓存前缀稳定），所以角色禁了的扩展也列着，调用时会如实说关了。
    /// 与各工具 schema 的一致性由测试对照
    /// </summary>
    public const string CallUsage =
        "内置命令（`?` = 可选）：\n" +
        "- browser.navigate {url, view?} · browser.snapshot {view?, find?} · browser.act {action, target?, value?} " +
        "· browser.handoff {task}. act 的 action 取 click | fill | select | check | uncheck | press | hover | scroll " +
        "| back；target 是最近一份 outline 里的 ref，如 e5。\n" +
        "- desktop.windows {} · desktop.snapshot {window, view?, find?} · desktop.act {window, action, target?, value?, input?} " +
        "· desktop.open {target} · desktop.handoff {window, task}. window 是 windows 列出的编号，如 w3。\n" +
        "- image.generate {prompt, images?, aspectRatio?}\n" +
        "- audio.synthesize {text, voice?, tone?, speed?} · audio.transcribe {path, subtitles?}\n" +
        "示例：" + CallExample;

    /// <summary>全部内置扩展名，按系统提示与帮助里的顺序</summary>
    public static readonly IReadOnlyList<string> Names = [Browser, Desktop, Image, Audio];

    private static readonly Dictionary<string, IReadOnlyList<(string Command, string ToolName)>> Commands = new()
    {
        [Browser] =
        [
            (BrowserSnapshot, BrowserTools.SnapshotToolName), (BrowserNavigate, BrowserTools.NavigateToolName),
            (BrowserAct, BrowserTools.ActToolName), (BrowserHandoff, BrowserTools.HandoffToolName),
        ],
        [Desktop] =
        [
            (DesktopWindows, DesktopTools.WindowsToolName), (DesktopSnapshot, DesktopTools.SnapshotToolName),
            (DesktopAct, DesktopTools.ActToolName), (DesktopOpen, DesktopTools.OpenToolName),
            (DesktopHandoff, DesktopTools.HandoffToolName),
        ],
        [Image] = [(ImageGenerate, ImageGenerationTool.ToolName)],
        [Audio] = [(AudioSynthesize, SynthesizeSpeechTool.ToolName), (AudioTranscribe, TranscribeAudioTool.ToolName)],
    };

    /// <summary>
    /// 一句话说明（系统提示里那行名单用）
    /// </summary>
    /// <param name="name">扩展名</param>
    /// <returns>说明</returns>
    public static string Summary(string name) => name switch
    {
        Browser => "操作一个真实的 Chrome 窗口：打开网页、点按钮、填表、读正文、截图",
        Desktop => "看、操作电脑上别的应用的窗口：读界面、点按钮、填字、按快捷键、启动应用",
        Image => "生成或修改图片",
        Audio => "用角色的声音配音成音频文件；把音频转写成文字或字幕",
        _ => string.Empty,
    };

    /// <summary>
    /// 规范写法（不区分大小写地认）
    /// </summary>
    /// <param name="name">模型写的扩展名</param>
    /// <returns>内置扩展名；不是内置扩展为 null</returns>
    public static string? Canonical(string name) =>
        Names.FirstOrDefault(x => string.Equals(x, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 命令的规范写法：认命令名，也认原工具名（模型照着旧习惯写 BrowserAct 时）
    /// </summary>
    /// <param name="extension">内置扩展名（规范写法）</param>
    /// <param name="command">模型写的命令名</param>
    /// <returns>命令名；没有这条命令为 null</returns>
    public static string? CanonicalCommand(string extension, string command)
    {
        if (!Commands.TryGetValue(extension, out IReadOnlyList<(string Command, string ToolName)>? commands)) return null;
        string wanted = command.Trim();
        foreach ((string name, string toolName) in commands)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toolName, wanted, StringComparison.OrdinalIgnoreCase)) return name;
        }

        return null;
    }

    /// <summary>
    /// 内置命令背后的原工具名
    /// </summary>
    /// <param name="extension">扩展名（规范写法）</param>
    /// <param name="command">命令名（规范写法）</param>
    /// <returns>原工具名；不是内置命令为 null</returns>
    public static string? ToolNameOf(string extension, string command) =>
        Commands.TryGetValue(extension, out IReadOnlyList<(string Command, string ToolName)>? commands)
            ? commands.FirstOrDefault(x => x.Command == command).ToolName
            : null;

    /// <summary>
    /// 在提示与结果文字里怎么写一条命令（<c>browser.handoff</c>）
    /// </summary>
    /// <param name="extension">扩展名</param>
    /// <param name="command">命令名</param>
    /// <returns>写法</returns>
    public static string Mention(string extension, string command) => $"{extension}.{command}";

    /// <summary>
    /// 浏览器扩展：一个会话一个标签页
    /// </summary>
    /// <param name="host">浏览器宿主</param>
    /// <param name="sessionId">会话 id</param>
    /// <param name="spillDirectory">快照超长落盘、截图存放的目录</param>
    /// <param name="screenshots">截图之后怎么看；看不了图为 None，不提供截图</param>
    /// <param name="paths">路径口径：上传只认工作区与草稿目录里的文件；不给就不能上传</param>
    /// <returns>扩展</returns>
    internal static BuiltinExtension CreateBrowser(IBrowserHost host, string sessionId, string spillDirectory,
        EReadImageMode screenshots, AgentPathResolver? paths = null) =>
        new(Browser,
            "带独立配置文件的真实 Chrome 窗口，一个会话一个标签页；用户在那里登录过的站点保持登录。" +
            "先 navigate（或 snapshot 重读已打开的页面），再按最新 outline 里的 ref 对元素 act。" +
            "snapshot 从不问用户；navigate 和 act 除非用户已为本会话放行浏览器，否则会询问；" +
            "handoff 和上传总是等用户。",
            Bind(Browser, BrowserTools.Create(host, sessionId, spillDirectory, screenshots, paths)));

    /// <summary>
    /// 桌面扩展：窗口编号全应用一份，快照按会话记
    /// </summary>
    /// <param name="host">桌面宿主</param>
    /// <param name="sessionId">会话 id</param>
    /// <param name="spillDirectory">快照超长落盘的目录</param>
    /// <param name="paths">路径口径：打开文件只认工作区与草稿目录</param>
    /// <param name="screenshots">截图之后怎么看；看不了图为 None，不提供截图</param>
    /// <returns>扩展；系统不支持或没授权时照样列出、调用时说明原因</returns>
    internal static BuiltinExtension CreateDesktop(DesktopHost host, string sessionId, string spillDirectory,
        AgentPathResolver? paths = null, EReadImageMode screenshots = EReadImageMode.None) =>
        new(Desktop,
            "用户电脑上别的应用的窗口。先 windows 列出窗口（编号如 w3），再 snapshot 读它的界面（ref 如 e5），" +
            "然后按 ref 对它 act。windows 与 snapshot 从不问用户；act 与 open 除非用户已为本会话放行这个应用，否则会询问；" +
            "input \"real\"（真实鼠标键盘）与 handoff 总是等用户。密码管理、系统设置、终端与本应用自身的窗口不能碰。",
            Bind(Desktop, DesktopTools.Create(host, sessionId, spillDirectory, paths, screenshots)))
        {
            // 没授权这类会变的原因不在装配时定死：用户中途授权了，下一次调用就能用（宿主每次调用现查）
            Unavailable = host.IsSupported ? null : host.Unavailable,
        };

    /// <summary>
    /// 生图扩展
    /// </summary>
    /// <param name="paths">路径口径</param>
    /// <param name="progress">把进度交给界面；没有界面在看为 null</param>
    /// <param name="configured">配了生图模型（没配照样列出，调用时说明去哪儿配）</param>
    /// <returns>扩展</returns>
    internal static BuiltinExtension CreateImage(AgentPathResolver paths, Action<string, ImageProgress>? progress,
        bool configured) =>
        new(Image, "根据提示生成图像，或传入路径编辑已有图像。",
            Bind(Image, [ImageGenerationTool.Create(paths, progress)]))
        {
            Unavailable = configured
                ? null
                : "未配置图像生成模型。告诉用户在「设置 → 图像生成」里添加一个。",
        };

    /// <summary>
    /// 音频扩展：配音要知道「自己」是谁（不点名时用自己的声音）；没有角色时只给转写
    /// </summary>
    /// <param name="paths">路径口径</param>
    /// <param name="self">自己；没有为 null</param>
    /// <returns>扩展</returns>
    internal static BuiltinExtension CreateAudio(AgentPathResolver paths, CharacterData? self)
    {
        List<AITool> tools = new();
        if (self != null) tools.Add(SynthesizeSpeechTool.Create(paths, self));
        tools.Add(TranscribeAudioTool.Create(paths));
        return new BuiltinExtension(Audio,
            "synthesize 用角色的声音把文字合成 WAV 文件（不出声播放）；" +
            "transcribe 把音频文件转成文字，可选带 SRT 字幕。结果保存在草稿目录下。",
            Bind(Audio, tools));
    }

    /// <summary>
    /// 列在名单里、这次却用不了的内置扩展（缺宿主之类）：帮助与调用都如实说原因
    /// </summary>
    /// <param name="name">扩展名</param>
    /// <param name="reason">用不了的原因（给模型看）</param>
    /// <returns>扩展</returns>
    internal static BuiltinExtension Unavailable(string name, string reason) =>
        new(name, string.Empty, []) { Unavailable = reason };

    // 按原工具名对回命令名：工具改名而对照表没跟上时这里当场炸，而不是悄悄少一条命令
    private static IReadOnlyList<ExtensionCommand> Bind(string extension, IEnumerable<AITool> tools) =>
        tools.Cast<AIFunction>().Select(function => new ExtensionCommand(
            Commands[extension].Single(x => x.ToolName == function.Name).Command, function)).ToList();
}
