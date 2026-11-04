/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Mcp;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 会话权限档(产品概念),底层映射到框架的 AutoApprovalRules 组合
/// </summary>
public enum EAgentPermissionMode
{
    /// <summary>只读/计划:仅自动放行只读工具,其余逐一审批</summary>
    ReadOnly,

    /// <summary>自动编辑:<b>工作区内</b>的文件写入自动放行,shell 与越界写入仍需审批</summary>
    AutoEdit,

    /// <summary>完全自动:全部自动放行,只有<b>越界写入</b>要用户点一次</summary>
    FullAuto,
}

/// <summary>
/// 三档权限 → 框架 ToolApprovalAgentOptions.AutoApprovalRules 的映射器。
/// 另支持定时任务的 shell 命令预授权(glob 匹配)。
///
/// <b>档位语义只有这一处定义</b>——主代理与子代理都从这里取规则,否则"完全自动"在两边
/// 会渐渐变成两个意思(有不变量测试钉住)。
///
/// 一条贯穿三档的硬规则:<b>工作区外的写入永不自动放行</b>，唯一的例外是会话自己的产出房间
/// （<c>approvedWriteRoot</c>，程序管辖、有界、随会话删除——与"磁盘任何地方"不是一回事）。
/// 扩展调用（<see cref="ExtensionCalls.CallName"/>）不归档位管，逐条按风险判（ADR 0079，见 <see cref="ExtensionRisk"/>）。
/// 用户点一次现成的"本会话允许"之后框架自己会把该工具整会话放行,所以这里不需要任何额外状态。
/// 它是有意做成"减法"而非"加一条否决规则"的——框架的 AutoApprovalRules 是<b>或</b>语义,
/// 任一条返回 true 即放行,加否决规则拦不住 <c>AllToolsAutoApprovalRule</c>,
/// 只能把那条全放行规则本身换掉。
/// </summary>
public static class ApprovalModeMapper
{
    /// <summary>
    /// 构建给定权限档的自动放行规则集
    /// </summary>
    /// <param name="mode">权限档</param>
    /// <param name="workspaceRoot">工作目录绝对路径,用于判定写入是否越界;空串表示无从判定(一律要审批)</param>
    /// <param name="preAuthorizedShellPatterns">预授权的 shell 命令 glob 模式(定时任务用),可空</param>
    /// <param name="sessionShellApprovalSource">会话级放行的 shell 命令模式来源
    /// (审批卡片"记住同类命令"写入,规则每次执行时现取现用,变化无需重建装配),可空</param>
    /// <param name="approvedWriteRoot">会话自己的产出房间绝对路径,落在里面的写入视为界内;
    /// 空串表示无豁免(老行为)。只认这一间,不认整棵产出树</param>
    /// <param name="memoryWriteRoot">记忆目录绝对路径(ADR 0028):落在里面的<b>写</b>入
    /// 任何权限档自动放行;空串表示无豁免。范围只认 <c>Memory/</c> 这一格,删除不在此列(走 shell)</param>
    /// <param name="sessionExtensionApprovalSource">本会话放行过的扩展名来源,可空</param>
    /// <param name="sessionWorktreeRootsSource">本会话登记的 worktree 来源(worktree 扩展写入,
    /// 落在里面、且登记工作区与当前工作区一致的写入视为界内;规则每次执行时现取现用,变化无需重建装配),可空</param>
    /// <param name="worktreesRoot">worktree 根（create 的默认落盘处）;空串表示没有。
    /// 必须与执行层同一份（<see cref="AgentAssemblyPlan.WorktreesRoot"/>），否则模型写 <c>$WORKTREES/…</c> 时
    /// 执行落在树里、审批判成界外</param>
    /// <param name="unattendedSource">无人在场的来源（定时运行、被定时运行派出、离席）：每次判定现取；
    /// 「只认扩展不再询问」那一档在这一刻直接拒，可空</param>
    /// <returns>规则列表</returns>
    public static List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> BuildRules(
        EAgentPermissionMode mode, string workspaceRoot = "",
        IReadOnlyList<string>? preAuthorizedShellPatterns = null,
        Func<IReadOnlyList<string>?>? sessionShellApprovalSource = null,
        string approvedWriteRoot = "", string memoryWriteRoot = "",
        Func<IReadOnlyList<string>?>? sessionExtensionApprovalSource = null,
        Func<IReadOnlyList<ChatSession.SessionWorktreeGrant>?>? sessionWorktreeRootsSource = null,
        string worktreesRoot = "", Func<bool>? unattendedSource = null) =>
        BuildRules(() => mode, workspaceRoot, preAuthorizedShellPatterns, sessionShellApprovalSource,
            approvedWriteRoot, memoryWriteRoot, sessionExtensionApprovalSource, sessionWorktreeRootsSource,
            worktreesRoot, unattendedSource);

    /// <summary>
    /// 同上，但档位每次判断时现取：用户中途改档（或群改档推给成员），下一条调用就按新档来，
    /// 不必等下一轮重新装配——跑到一半调严却还按旧档放行，界面与实际就对不上了
    /// </summary>
    /// <param name="modeSource">当前权限档来源</param>
    /// <param name="workspaceRoot">工作目录绝对路径</param>
    /// <param name="preAuthorizedShellPatterns">预授权的 shell 命令 glob 模式,可空</param>
    /// <param name="sessionShellApprovalSource">会话级放行的 shell 命令模式来源,可空</param>
    /// <param name="approvedWriteRoot">会话自己的产出房间绝对路径</param>
    /// <param name="memoryWriteRoot">记忆目录绝对路径</param>
    /// <param name="sessionExtensionApprovalSource">本会话放行过的扩展名来源,可空</param>
    /// <param name="sessionWorktreeRootsSource">本会话登记的 worktree 来源,可空</param>
    /// <param name="worktreesRoot">worktree 根（create 的默认落盘处）;空串表示没有</param>
    /// <param name="unattendedSource">无人在场的来源：每次判定现取（离席可能在一轮中途开始或结束），可空</param>
    /// <returns>规则列表</returns>
    public static List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> BuildRules(
        Func<EAgentPermissionMode> modeSource, string workspaceRoot = "",
        IReadOnlyList<string>? preAuthorizedShellPatterns = null,
        Func<IReadOnlyList<string>?>? sessionShellApprovalSource = null,
        string approvedWriteRoot = "", string memoryWriteRoot = "",
        Func<IReadOnlyList<string>?>? sessionExtensionApprovalSource = null,
        Func<IReadOnlyList<ChatSession.SessionWorktreeGrant>?>? sessionWorktreeRootsSource = null,
        string worktreesRoot = "", Func<bool>? unattendedSource = null)
    {
        // 产出房间就是草稿目录:简写 $DRAFT / $MEMORY / $WORKTREES 按它们展开,与工具执行时落到同一处
        AgentPathResolver paths = new(workspaceRoot, approvedWriteRoot, memoryWriteRoot,
            worktreesRoot: worktreesRoot);
        string memory = paths.MemoryRoot;

        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules = new()
        {
            // 任何档位:技能只读工具自动放行。
            // 文件读取工具(Read/Glob/Grep)压根没包审批,不需要规则;写工具(Write/Edit)由
            // PermissiveFileAccessTools 包了 ApprovalRequiredAIFunction,放行与否全看下面这几条
            AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule,
        };

        // 全放行,但越界写入除外——定时任务是代码写死的档位,用户没机会为它选,
        // 而无人值守下越界写入没人拦就真的没人拦了。扩展调用也除外:它不归档位管,由下一条逐条判
        rules.Add(context => new ValueTask<bool>(
            modeSource() == EAgentPermissionMode.FullAuto &&
            !ExtensionCalls.IsCall(context.FunctionCallContent) &&
            !IsOutOfWorkspaceWrite(context.FunctionCallContent, paths, sessionWorktreeRootsSource?.Invoke())));

        // 扩展:只读的三档都不问;一般的本会话放行过这个扩展就不问;逐项的(桌面真实键鼠)只认单独放行过的那一格;
        // 只认「扩展不再询问」的(本机服务、界外本地文件)看全局那一格;内网设备与非 http(s) 永远问;
        // 完全自动档免审批的(worktree.create)只在 FullAuto 放行
        // 界内文件页面的判据夹在 context 里递下去：分级与放行两侧必须同一份（见 InScopePredicate）
        ExtensionRiskContext riskContext = new()
        {
            McpReadOnly = (server, tool) => McpManager.Instance.IsReadOnlyTool(server, tool, workspaceRoot),
            IsPathInScope = InScopePredicate(paths, sessionWorktreeRootsSource),
            IsUnattended = unattendedSource,
        };
        rules.Add(context => new ValueTask<bool>(
            IsApprovedExtensionCall(context.FunctionCallContent, sessionExtensionApprovalSource, riskContext,
                fullAuto: modeSource() == EAgentPermissionMode.FullAuto)));

        // 自动编辑:工作区内的写入放行。这一档曾经<b>一条规则都不加</b>,于是与只读档行为完全一致:
        // 枚举注释写着"文件读写自动放行",实际每次编辑都弹卡。更要命的是定时任务写死用这一档而
        // 无头执行一律拒绝审批,净效果是定时任务所有文件写入都被拒
        rules.Add(context => new ValueTask<bool>(
            modeSource() == EAgentPermissionMode.AutoEdit &&
            IsMutatingFileTool(context.FunctionCallContent) &&
            !IsOutOfWorkspaceWrite(context.FunctionCallContent, paths, sessionWorktreeRootsSource?.Invoke())));

        // 记忆目录:任何权限档可写(ADR 0028)——记忆是 agent 的工作台,只读/计划档也得能记笔记。
        // 范围只认 Memory/ 这一格;删除不在这里,走 shell 由命令行审批纪律兜。
        if (memory.Length > 0)
        {
            rules.Add(context =>
                new ValueTask<bool>(IsUnderMemory(context.FunctionCallContent, paths, memory)));
        }

        if (preAuthorizedShellPatterns is { Count: > 0 })
        {
            List<string> patterns = new(preAuthorizedShellPatterns);
            rules.Add(context => new ValueTask<bool>(MatchesShellPattern(context.FunctionCallContent, patterns)));
        }

        if (sessionShellApprovalSource != null)
        {
            rules.Add(context =>
                new ValueTask<bool>(MatchesShellPattern(context.FunctionCallContent, sessionShellApprovalSource())));
        }

        return rules;
    }

    /// <summary>
    /// 这次扩展调用能不能不问用户（ADR 0079）
    /// </summary>
    /// <param name="call">工具调用</param>
    /// <param name="sessionApprovalSource">本会话放行过的扩展名来源,可空</param>
    /// <param name="context">判风险要用的外部事实(MCP 只读声明、界内路径判据)</param>
    /// <param name="fullAuto">当前是否完全自动档：<see cref="EExtensionRisk.FullAutoOnly"/> 在这一档直接放行</param>
    /// <returns>放行为 true；不是扩展调用为 false</returns>
    internal static bool IsApprovedExtensionCall(FunctionCallContent call,
        Func<IReadOnlyList<string>?>? sessionApprovalSource, ExtensionRiskContext context,
        bool fullAuto = false)
    {
        EExtensionRisk? risk = ExtensionRisk.Classify(call, context);
        if (risk == EExtensionRisk.ReadOnly) return true;
        if (risk == EExtensionRisk.FullAutoOnly)
        {
            if (fullAuto) return true;
            // 非全自动档：按 Standard 走（问一次，本会话放行过这个扩展就不再问）
            return ExtensionCalls.TryRead(call, out ExtensionInvocation? approvedInv) &&
                   ExtensionRisk.IsApproved(sessionApprovalSource?.Invoke(),
                       ExtensionRisk.ApprovalKey(approvedInv, call), coveredByAll: true);
        }

        // 只认「本会话扩展不再询问」那一格：点单个扩展的「本会话允许」不够（去本机服务、读工作区外的本地文件）。
        // 无人在场时一律拒：那一下的同意得是人亲手勾的（见 ExtensionRiskContext.IsUnattended，离席与定时都是）
        if (risk == EExtensionRisk.SessionWideOnly)
        {
            return context.IsUnattended?.Invoke() != true &&
                   ExtensionRisk.IsApproved(sessionApprovalSource?.Invoke(), ExtensionCalls.AllExtensions);
        }

        if (risk is not (EExtensionRisk.Standard or EExtensionRisk.PerScope)) return false;
        return ExtensionCalls.TryRead(call, out ExtensionInvocation? invocation) &&
               ExtensionRisk.IsApproved(sessionApprovalSource?.Invoke(), ExtensionRisk.ApprovalKey(invocation, call),
                   coveredByAll: risk == EExtensionRisk.Standard);
    }

    /// <summary>
    /// 从一条 shell 命令派生"同类命令"的放行模式:取前两个词加通配
    /// (如 "git status --short" → "git status*";单词命令 "ls" → "ls*")。
    /// 只取头部是刻意的——"git*" 会连 push 一起放行,过宽;前两词恰好圈住"同一子命令"。
    /// </summary>
    /// <param name="command">命令原文</param>
    /// <returns>glob 模式;空命令返回空串</returns>
    public static string DeriveCommandPattern(string command)
    {
        string[] tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return string.Empty;
        return tokens.Length == 1 ? $"{tokens[0]}*" : $"{tokens[0]} {tokens[1]}*";
    }

    private static bool MatchesShellPattern(FunctionCallContent functionCall, IReadOnlyList<string>? patterns)
    {
        if (patterns is not { Count: > 0 }) return false;
        if (!CharacterRunnerFactory.IsShellCommandTool(functionCall.Name)) return false;
        string? command = ExtractCommand(functionCall.Arguments);
        if (string.IsNullOrEmpty(command)) return false;
        return patterns.Any(pattern => GlobMatch(pattern, command));
    }

    /// <summary>
    /// 从 shell 工具调用参数中取命令原文(审批卡片派生放行模式也用它)
    /// </summary>
    /// <param name="arguments">工具调用参数</param>
    /// <returns>命令文本;取不到为 null</returns>
    public static string? ExtractCommand(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("command", out object? value)) return null;
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }

    /// <summary>
    /// 从文件工具调用参数中取目标路径(审批越界判定用)
    /// </summary>
    /// <param name="arguments">工具调用参数</param>
    /// <returns>路径文本;取不到为 null</returns>
    public static string? ExtractFilePath(IDictionary<string, object?>? arguments)
    {
        if (arguments == null) return null;
        if (!arguments.TryGetValue("filePath", out object? value) &&
            !arguments.TryGetValue("file_path", out value)) return null;

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString(),
        };
    }

    private static bool IsMutatingFileTool(FunctionCallContent functionCall)
        => FileToolNames.Mutating.Contains(functionCall.Name);

    /// <summary>
    /// 这次调用是不是越界写入（离席时化身据此不替用户点，ADR 0055）。与放行规则同一份口径，草稿目录算界内。
    /// 化身替用户点的那一口不认会话登记的 worktree（无人能确认那是否用户本意），沿用老行为
    /// </summary>
    /// <param name="functionCall">工具调用</param>
    /// <param name="paths">发起调用的会话的路径口径</param>
    /// <returns>越界写入为 true</returns>
    public static bool IsOutOfWorkspaceWrite(FunctionCallContent functionCall, AgentPathResolver paths) =>
        IsOutOfWorkspaceWrite(functionCall, paths, worktreeGrants: null);

    /// <summary>
    /// 同上，另给本会话登记的 worktree（化身对派活者自己的 worktree 放行；与放行规则同一份口径）
    /// </summary>
    /// <param name="functionCall">工具调用</param>
    /// <param name="paths">发起调用的会话的路径口径</param>
    /// <param name="worktreeGrants">会话登记的 worktree（含登记工作区）；落在里面且工作区一致的写入视为界内</param>
    /// <returns>越界写入为 true</returns>
    public static bool IsOutOfWorkspaceWrite(FunctionCallContent functionCall, AgentPathResolver paths,
        IReadOnlyList<ChatSession.SessionWorktreeGrant>? worktreeGrants) =>
        IsOutOfWorkspaceWriteCore(functionCall, paths, worktreeGrants);

    /// <summary>
    /// 这次调用是否是「工作区外的写入」。
    ///
    /// 判据故意保守:是写工具、但路径<b>无从判定</b>(非法字符、过长)时也算越界——宁可多问一次,
    /// 也不能让一个畸形参数悄悄越界落盘。
    ///
    /// 例外是<b>根本没有路径</b>:<c>filePath</c> 是两个写工具的必填参数,缺了参数绑定当场失败,
    /// 这次调用什么都写不了。框架解析不了参数 JSON(太长被截断、转义写坏)时交来的正是这种空参数调用,
    /// 拦下来只会弹一张空卡,批了也还是报错——不如直接放行,让模型立刻拿到报错重发。
    ///
    /// 只比较规范化后的路径,<b>不解析符号链接</b>:工作区里一个指向外部的软链能绕过这条判据。
    /// 接受这个缺口,因为能这么干的模型同样能直接用 shell,而真正的边界是"用户点了那一下"——
    /// 每次审批都去 realpath 一趟只是把成本花在挡不住的地方。
    /// </summary>
    /// <param name="worktreeGrants">会话登记的 worktree（已规范化）；落在里面、且登记工作区与当前有效工作区
    /// 一致才视为界内；空则无豁免。换绑工作区后旧登记自然失效</param>
    /// <returns>越界写入为 true</returns>
    private static bool IsOutOfWorkspaceWriteCore(FunctionCallContent functionCall, AgentPathResolver paths,
        IReadOnlyList<ChatSession.SessionWorktreeGrant>? worktreeGrants)
    {
        if (!IsMutatingFileTool(functionCall)) return false;
        if (paths.WorkspaceRoot.Length == 0) return true;

        string? path = ExtractFilePath(functionCall.Arguments);
        if (path == null) return false;

        //路径非法(含非法字符/过长)一样交给用户看一眼
        if (!paths.TryResolve(path, out string full)) return true;

        return !InScopePredicate(paths, () => worktreeGrants)(full);
    }

    /// <summary>
    /// 「这个绝对路径算不算本会话界内」的判据：工作区、草稿房间、登记且同工作区的 worktree。
    /// <b>只有这一处定义</b>：审批放行、风险分级（浏览器的界内文件页面）与离席化身都问它，
    /// 各写一份就会出现「规则放行了、卡上却写着每次都问」。
    /// 没绑工作区时一律 false：沙箱里没有「界内」可言。
    /// </summary>
    /// <param name="paths">发起调用的会话的路径口径</param>
    /// <param name="worktreeGrantsSource">会话登记的 worktree 来源（每次判定现取，变化无需重建装配），可空</param>
    /// <returns>判定函数</returns>
    public static Func<string, bool> InScopePredicate(AgentPathResolver paths,
        Func<IReadOnlyList<ChatSession.SessionWorktreeGrant>?>? worktreeGrantsSource = null) => full =>
    {
        if (paths.WorkspaceRoot.Length == 0) return false;
        // 入参必须是绝对路径，且先规范化再比：判据是字面前缀比较（见 AgentPathResolver.IsUnder），
        // 带着 ".." 传进来就能跳出工作区。相对路径与非法路径按「不在界内」处理（安全侧）
        string normalized = NormalizeInScopePath(full);
        if (normalized.Length == 0) return false;
        if (AgentPathResolver.IsUnder(normalized, paths.WorkspaceRoot)) return true;
        if (AgentPathResolver.IsUnder(normalized, paths.DraftRoot)) return true;
        IReadOnlyList<ChatSession.SessionWorktreeGrant>? grants = worktreeGrantsSource?.Invoke();
        return grants is { Count: > 0 } && grants.Any(g =>
            g is { Root.Length: > 0, Workspace.Length: > 0 } &&
            SameWorkspace(g.Workspace, paths.WorkspaceRoot) &&
            AgentPathResolver.IsUnder(normalized, g.Root));
    };

    // 规范化到与根同一口径（AgentPathResolver 对根用的也是 GetFullPath）；拿不准的一律当不在界内。
    // 先截掉 ?query / #fragment：`file:///C:/ws/x.html?x=1` 的 LocalPath 会把 query 带进来，
    // GetFullPath 碰到 Windows 上非法的 ? 就抛——那会把界内页面误判成界外，白白多问
    private static string NormalizeInScopePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        int cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];
        if (!Path.IsPathRooted(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// 登记工作区与当前有效工作区是否同一（与 <see cref="AgentPathResolver.IsUnder"/> 同一平台口径）
    private static bool SameWorkspace(string granted, string current)
    {
        StringComparison comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string a = granted.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string b = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return a.Equals(b, comparison);
    }

    /// <summary>
    /// 记忆目录内的写是否成立(ADR 0028):写工具 + 目标落在记忆目录之内。
    /// 判据与 <see cref="IsOutOfWorkspaceWrite"/> 用同一份路径解析——相对路径按工作目录展开。
    /// </summary>
    private static bool IsUnderMemory(FunctionCallContent functionCall, AgentPathResolver paths, string memoryRoot)
    {
        // 只认写工具:规则是「或」语义,不看工具名的话,任何调用顺手带一个指向记忆目录的 filePath 就整条放行
        // (shell、扩展调用的接管都算在内)
        if (!IsMutatingFileTool(functionCall) || paths.WorkspaceRoot.Length == 0) return false;

        //路径非法:宁可当不在记忆里,让用户看一眼
        return paths.TryResolve(ExtractFilePath(functionCall.Arguments), out string full) &&
               AgentPathResolver.IsUnder(full, memoryRoot);
    }

    private static bool GlobMatch(string pattern, string input)
    {
        if (pattern == "*") return true;
        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.Singleline);
    }
}
