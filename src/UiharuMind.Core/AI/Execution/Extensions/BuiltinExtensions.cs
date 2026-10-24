/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Extensions;

/// <summary>
/// 内置扩展的注册表（ADR 0079）：每个扩展是一个类（<see cref="BuiltinExtension"/>），
/// 名字、命令、风险、场合、一句话说明都内聚在类里，这里只负责<b>登记与按名取回</b>。
///
/// 加新扩展 = 写一个 <c>XxxExtension</c> 类 + 在 <see cref="Catalog"/> 登记一行。
/// 不再需要去 ExtensionRisk / AgentToolsetBuilder / 界面 各处各插一段
/// </summary>
public static class BuiltinExtensions
{
    /// <summary>浏览器</summary>
    public const string Browser = "browser";

    /// <summary>生图</summary>
    public const string Image = "image";

    /// <summary>音频：配音成文件、转写</summary>
    public const string Audio = "audio";

    /// <summary>worktree：git worktree 的创建与登记</summary>
    public const string Worktree = "worktree";

    /// <summary>桌面：看、动别的应用的窗口（ADR 0084）</summary>
    public const string Desktop = "desktop";

    /// <summary>chatsession：查看本会话派出的子代理状态与群聊状态</summary>
    public const string ChatSession = "chatsession";

    /// <summary>目标模式（隐藏扩展）：模型自判结束目标模式</summary>
    public const string Goal = "goal";

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

    /// <summary>桌面：启动应用、打开文件。不叫 open：模型会读成「打开桌面扩展」，开局空参调用（冒烟里约一成）</summary>
    public const string DesktopLaunch = "launch";

    /// <summary>桌面：交给用户</summary>
    public const string DesktopHandoff = "handoff";

    /// <summary>生图：生成或修改</summary>
    public const string ImageGenerate = "generate";

    /// <summary>音频：配音成文件</summary>
    public const string AudioSynthesize = "synthesize";

    /// <summary>音频：转写</summary>
    public const string AudioTranscribe = "transcribe";

    /// <summary>worktree：一键创建并登记</summary>
    public const string WorktreeCreate = "create";

    /// <summary>worktree：登记自己已有的</summary>
    public const string WorktreeSet = "set";

    /// <summary>worktree：取消登记（动态切回）</summary>
    public const string WorktreeReset = "reset";

    /// <summary>worktree：删除并取消登记（清理）</summary>
    public const string WorktreeRemove = "remove";

    /// <summary>worktree：列出已登记的（带上次修改时间）</summary>
    public const string WorktreeList = "list";

    /// <summary>worktree：查询当前状态</summary>
    public const string WorktreeStatus = "status";

    /// <summary>worktree：把主工作区最新状态同步进已登记的 worktree</summary>
    public const string WorktreeSync = "sync";

    /// <summary>chatsession：本会话派出的子代理</summary>
    public const string ChatSessionList = "list";

    /// <summary>chatsession：群聊状态（留「本会话发起的群」过滤的口子）</summary>
    public const string ChatSessionGroups = "list-groups";

    /// <summary>chatsession：已加载的模型列表（派活前选 model 用）</summary>
    public const string ChatSessionModels = "list-models";

    /// <summary>chatsession：角色列表（派活前选 character 用）</summary>
    public const string ChatSessionCharacters = "list-characters";

    /// <summary>chatsession：强行停止一个正在跑的子代理</summary>
    public const string ChatSessionStop = "stop";

    /// <summary>目标模式：结束（reason=done | needs_user，summary=给用户的交代）</summary>
    public const string GoalEnd = "end";

    /// <summary>一次调用的完整写法，报错与工具描述共用</summary>
    public const string CallExample =
        """{"name": "browser.act", "args": {"action": "fill", "target": "e3", "value": "hello"}}""";

    /// <summary>
    /// 全部内置扩展的规格（无资源依赖，可共享）。加扩展在这里登记一行
    /// </summary>
    public static readonly IReadOnlyList<BuiltinExtension> Catalog =
    [
        BrowserExtension.Spec, DesktopExtension.Spec, ImageExtension.Spec,
        AudioExtension.Spec, WorktreeExtension.Spec, ChatSessionExtension.Spec, GoalModeExtension.Spec,
    ];

    /// <summary>全部内置扩展名，按系统提示与帮助里的顺序（隐藏扩展不进名单）</summary>
    public static readonly IReadOnlyList<string> Names =
        Catalog.Where(x => !x.IsHidden).Select(x => x.Name).ToList();

    /// <summary>
    /// 按名字找扩展规格（不区分大小写地认，隐藏扩展单独认）。不是内置扩展为 null
    /// </summary>
    /// <param name="name">模型/界面写的扩展名</param>
    /// <returns>扩展规格</returns>
    public static BuiltinExtension? SpecOf(string name)
    {
        string wanted = name.Trim();
        return Catalog.FirstOrDefault(x => string.Equals(x.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 规范写法（不区分大小写地认）。隐藏扩展（<see cref="Goal"/>）不在广告名单里，这里单独认
    /// </summary>
    /// <param name="name">模型写的扩展名</param>
    /// <returns>内置扩展名；不是内置扩展为 null</returns>
    public static string? Canonical(string name) => SpecOf(name)?.Name;

    /// <summary>
    /// 命令的规范写法：认命令名，也认原工具名（模型照着旧习惯写 BrowserAct 时）
    /// </summary>
    /// <param name="extension">内置扩展名（规范写法）</param>
    /// <param name="command">模型写的命令名</param>
    /// <returns>命令名；没有这条命令为 null</returns>
    public static string? CanonicalCommand(string extension, string command) =>
        SpecOf(extension)?.CanonicalCommand(command);

    /// <summary>
    /// 内置命令背后的原工具名
    /// </summary>
    /// <param name="extension">扩展名（规范写法）</param>
    /// <param name="command">命令名（规范写法）</param>
    /// <returns>原工具名；不是内置命令为 null</returns>
    public static string? ToolNameOf(string extension, string command) =>
        SpecOf(extension)?.ToolNameOf(command);

    /// <summary>
    /// 在提示与结果文字里怎么写一条命令（<c>browser.handoff</c>）
    /// </summary>
    /// <param name="extension">扩展名</param>
    /// <param name="command">命令名</param>
    /// <returns>写法</returns>
    public static string Mention(string extension, string command) => $"{extension}.{command}";

    /// <summary>
    /// 一句话说明（系统提示里那份名单用）
    /// </summary>
    /// <param name="name">扩展名</param>
    /// <returns>说明</returns>
    public static string Summary(string name) => SpecOf(name)?.Summary ?? string.Empty;
}
