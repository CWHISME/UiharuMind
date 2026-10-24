/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution.Tools.Worktree;

/// <summary>
/// worktree 扩展的业务逻辑：创建（自动拉取/子模块/软链接）与登记（免审批）。
/// 唯一依赖是 <see cref="IWorktreeGitRunner"/>，因此命令序列与判定都可单测。
///
/// 边界刻意收在这里：<b>不改会话的工作目录、不动系统提示</b>——登记只把 worktree 根
/// 记进会话的 <see cref="ChatSession.SessionApprovedWorktreeRoots"/>，审批规则每次现取，
/// 于是「切过去 / 切回来」都动态生效，无需重建装配。
/// </summary>
internal sealed class WorktreeGitService
{
    private readonly IWorktreeGitRunner _git;

    public WorktreeGitService(IWorktreeGitRunner git) => _git = git;

    /// <summary>会话的有效工作区：群成员跟群走（GroupChatSessions.WorkspaceOf），其余取自己绑的</summary>
    private static string? WorkspaceOf(ChatSession session) => GroupChatSessions.WorkspaceOf(session);

    /// <summary>
    /// 一键创建 worktree：拉取 → 建树 → 子模块 → 软链接，然后登记为免审批
    /// </summary>
    /// <param name="session">会话（读工作目录、写登记）</param>
    /// <param name="name">worktree 目录名，同时默认作新分支名</param>
    /// <param name="baseBranch">基于的分支/提交；缺省当前 HEAD</param>
    /// <param name="branch">新分支名；缺省同 name（已存在则直接检出，不新建）</param>
    /// <param name="directory">落盘目录；缺省为数据目录里项目共享的 Worktrees 下（跟 memory 同家）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>给模型的文本</returns>
    public async Task<string> CreateAsync(ChatSession session, string? name, string? baseBranch,
        string? branch, string? directory, CancellationToken cancellationToken)
    {
        string workspace = WorkspaceOf(session)?.Trim() ?? string.Empty;
        if (workspace.Length == 0) return "Error: 会话没有绑定工作目录，无法创建 worktree。";
        string safeName = (name ?? string.Empty).Trim();
        if (safeName.Length == 0) return "Error: name 必填（worktree 目录名，同时默认作新分支名）。";

        string workspaceFull = Full(workspace, "工作目录");
        if (workspaceFull.Length == 0) return "Error: 工作目录路径不成形。";
        string worktreesRoot = WorktreeLayout.GetWorktreesRoot(workspaceFull);

        // 落盘范围锁在数据目录项目共享的 Worktrees 根内：建树会顺带登记为免审批，
        // 不能让它成为「模型给任意路径、登记后整棵子树免审批」的口子。想登记别处的树用 worktree.set（有同仓库校验）。
        // 相对 directory 按根解析（directory: "feature/foo" → &lt;根&gt;/feature/foo）；绝对路径必须落在根内
        string target = string.IsNullOrWhiteSpace(directory)
            ? DefaultDirectory(workspaceFull, safeName)
            : Full(directory.Trim(), "directory", worktreesRoot);
        if (target.Length == 0) return "Error: directory 路径不成形。";
        if (!AgentPathResolver.IsUnder(target, worktreesRoot))
        {
            return $"Error: directory 必须在项目共享的 Worktrees 根内：{worktreesRoot}\n" +
                   "落别处的树请先用 git 自己建好，再用 worktree.set 登记（set 会校验同一仓库）。";
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            return $"Error: 目标目录已存在且非空：{target}。换一个 name 或 directory。";

        string branchName = string.IsNullOrWhiteSpace(branch) ? safeName : branch.Trim();

        // 工作目录是不是 git 仓库：不是就走「纯副本」——worktree 是通用隔离副本，git 只是有则锦上添花
        // （分支/拉取/子模块/软链接都只在真有 git 时处理，不应该是前提）
        WorktreeGitResult repoCheck = await _git.RunAsync(workspaceFull, ["rev-parse", "--git-common-dir"], cancellationToken)
            .ConfigureAwait(false);
        if (!repoCheck.Succeeded)
        {
            return await CreateCopyWorktreeAsync(session, workspaceFull, target, cancellationToken).ConfigureAwait(false);
        }

        (string? error, List<string> steps, string branchNote) = await CreateGitWorktreeAsync(
            workspaceFull, target, branchName, baseBranch, cancellationToken).ConfigureAwait(false);
        if (error != null) return error;

        session.AddSessionApprovedWorktreeRoot(target);
        return $"已创建并登记 worktree：{target}\n" +
               $"分支：{branchName}（{branchNote}）\n" +
               $"步骤：\n  " + string.Join("\n  ", steps) + "\n" +
               "登记后 worktree 内的文件写入免审批（AutoEdit/FullAuto 档），子代理派活时自动继承这份登记。\n" +
               $"注意：本会话的工作目录（相对路径基准）没有变，仍是 {workspaceFull}；" +
               "在 worktree 里干活请用绝对路径。草稿目录 $DRAFT 与记忆目录 $MEMORY 都不变。\n" +
               "用 worktree.status 查询状态，用 worktree.list 看每个 worktree 的上次修改时间。";
    }

    /// <summary>
    /// git 仓库的 create：拉取 → 建树 → 子模块 → 软链接。返回要展示的步骤与分支说明；出错时 Error 非空
    /// </summary>
    /// <returns>Error（非空即失败）、Steps、BranchNote</returns>
    private async Task<(string? Error, List<string> Steps, string BranchNote)> CreateGitWorktreeAsync(
        string workspaceFull, string target, string branchName, string? baseBranch, CancellationToken cancellationToken)
    {
        List<string> steps = [];

        // 1) 拉取：无远程不算失败（继续基于本地创建），有远程拉到最新再建
        WorktreeGitResult fetch = await _git.RunAsync(workspaceFull, ["fetch", "origin", "--prune", "--tags"], cancellationToken)
            .ConfigureAwait(false);
        steps.Add(fetch.Succeeded
            ? "已拉取 origin（fetch --prune --tags）"
            : $"拉取 origin 未完成（{Brief(fetch)}）；继续基于本地创建");

        // 2) 建树：同名分支已存在就检出现有分支（不新建，忽略 base）；否则新建
        //    （优先跟踪 origin/ 上同名的，其次才从 base 创建）
        string branchNote;
        WorktreeGitResult branchCheck = await _git.RunAsync(workspaceFull,
            ["rev-parse", "--verify", $"refs/heads/{branchName}"], cancellationToken).ConfigureAwait(false);
        WorktreeGitResult add;
        if (branchCheck.Succeeded)
        {
            add = await _git.RunAsync(workspaceFull, ["worktree", "add", target, branchName], cancellationToken)
                .ConfigureAwait(false);
            branchNote = "已存在同名分支，直接检出";
        }
        else
        {
            // 定基准：先认本地，其次 origin/<base>，缺省 HEAD
            string baseRef = "HEAD";
            if (!string.IsNullOrWhiteSpace(baseBranch))
            {
                string wanted = baseBranch.Trim();
                WorktreeGitResult local = await _git.RunAsync(workspaceFull, ["rev-parse", "--verify", wanted], cancellationToken)
                    .ConfigureAwait(false);
                if (local.Succeeded)
                {
                    baseRef = wanted;
                }
                else
                {
                    WorktreeGitResult remote = await _git.RunAsync(workspaceFull,
                        ["rev-parse", "--verify", $"origin/{wanted}"], cancellationToken).ConfigureAwait(false);
                    if (!remote.Succeeded)
                    {
                        return ($"Error: 找不到 base '{wanted}'（本地与 origin/ 都没有）。" +
                                "确认分支名，或先 fetch 一次再试。", steps, string.Empty);
                    }

                    baseRef = $"origin/{wanted}";
                }
            }

            WorktreeGitResult originCheck = await _git.RunAsync(workspaceFull,
                ["rev-parse", "--verify", $"origin/{branchName}"], cancellationToken).ConfigureAwait(false);
            if (originCheck.Succeeded)
            {
                add = await _git.RunAsync(workspaceFull,
                    ["worktree", "add", "-b", branchName, target, $"origin/{branchName}"], cancellationToken)
                    .ConfigureAwait(false);
                branchNote = $"新建本地分支 {branchName}，跟踪 origin/{branchName}";
            }
            else
            {
                add = await _git.RunAsync(workspaceFull,
                    ["worktree", "add", "-b", branchName, target, baseRef], cancellationToken)
                    .ConfigureAwait(false);
                branchNote = $"新建分支 {branchName}，基于 {baseRef}";
            }
        }

        if (!add.Succeeded)
        {
            return ($"Error: 创建 worktree 失败：{Brief(add)}", steps, string.Empty);
        }

        steps.Add($"已创建 worktree {target}（{branchNote}）");

        // 3) 子模块：初始化并递归拉取
        WorktreeGitResult submodule = await _git.RunAsync(target, ["submodule", "update", "--init", "--recursive"],
            cancellationToken).ConfigureAwait(false);
        steps.Add(submodule.Succeeded
            ? "子模块已初始化（submodule update --init --recursive）"
            : $"子模块初始化未完成：{Brief(submodule)}");

        // 4) 软链接：仓库里有 120000 模式（符号链接）才动；Windows 上没开开发者模式会失败，照实报告
        WorktreeGitResult modes = await _git.RunAsync(target, ["ls-files", "-s"], cancellationToken).ConfigureAwait(false);
        if (modes.Succeeded && modes.Output.Split('\n').Any(line =>
                line.TrimStart().StartsWith("120000", StringComparison.Ordinal)))
        {
            WorktreeGitResult config = await _git.RunAsync(target, ["config", "core.symlinks", "true"], cancellationToken)
                .ConfigureAwait(false);
            WorktreeGitResult checkout = await _git.RunAsync(target, ["checkout-index", "-f", "-a"], cancellationToken)
                .ConfigureAwait(false);
            steps.Add(config.Succeeded && checkout.Succeeded
                ? "软链接已修复（core.symlinks=true + 重新落盘）"
                : $"软链接修复未完成：{Brief(config)}；{Brief(checkout)}");
        }

        return (null, steps, branchNote);
    }

    /// <summary>
    /// 工作目录不是 git 仓库时的 create：把内容复制成隔离副本并登记。
    /// 不做 git 处理（没有 git 也就无从分支/子模块/软链接）
    /// </summary>
    private async Task<string> CreateCopyWorktreeAsync(ChatSession session, string workspaceFull, string target,
        CancellationToken cancellationToken)
    {
        List<string> excluded = [];
        try
        {
            await CopyDirectoryAsync(workspaceFull, target, excluded, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return $"Error: 复制工作目录失败：{e.Message}";
        }

        session.AddSessionApprovedWorktreeRoot(target);
        string excludedNote = excluded.Count > 0
            ? $"\n已跳过常见大目录：{string.Join("、", excluded)}"
            : string.Empty;
        return $"已创建并登记 worktree（纯副本，工作目录不是 git 仓库）：{target}{excludedNote}\n" +
               "已复制工作目录内容作为隔离副本；未做 git 分支/拉取/子模块/软链接处理（没有 git，也就不做）。\n" +
               "登记后 worktree 内的文件写入免审批（AutoEdit/FullAuto 档），子代理派活时自动继承这份登记。\n" +
               $"注意：本会话的工作目录（相对路径基准）没有变，仍是 {workspaceFull}；" +
               "在 worktree 里干活请用绝对路径。草稿目录 $DRAFT 与记忆目录 $MEMORY 都不变。\n" +
               "用 worktree.status 查询状态。";
    }

    /// <summary>复制时跳过的顶层目录（各生态的构建/缓存产物；复制是给没有 git 的工作目录做隔离副本，
    /// 不是版本控制，只能整份拷、再排除已知会又大又没用的）</summary>
    private static readonly HashSet<string> CopyExcludes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "node_modules", "bin", "obj", "dist", "build",
        "TestResults", ".venv", "venv", "__pycache__", ".pytest_cache", ".gradle", ".mypy_cache",
        "Packages", "Library", "Temp", "Logs", "DerivedData",
    };

    private static async Task CopyDirectoryAsync(string source, string target, List<string> excluded,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(entry);
            string destination = Path.Combine(target, name);
            if (Directory.Exists(entry))
            {
                if (CopyExcludes.Contains(name))
                {
                    excluded.Add(name);
                    continue;
                }

                await CopyDirectoryAsync(entry, destination, excluded, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                File.Copy(entry, destination, overwrite: false);
            }
        }
    }

    /// <param name="session">会话（读工作目录、写登记）</param>
    /// <param name="path">worktree 根目录的绝对路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>给模型的文本</returns>
    public async Task<string> SetAsync(ChatSession session, string? path, CancellationToken cancellationToken)
    {
        string workspace = WorkspaceOf(session)?.Trim() ?? string.Empty;
        if (workspace.Length == 0) return "Error: 会话没有绑定工作目录。";
        if (string.IsNullOrWhiteSpace(path)) return "Error: path 必填（worktree 根目录的绝对路径）。";

        string workspaceFull = Full(workspace, "工作目录");
        if (workspaceFull.Length == 0) return "Error: 工作目录路径不成形。";
        string wanted = Full(path.Trim(), "path", workspaceFull);
        if (wanted.Length == 0) return "Error: path 路径不成形。";
        if (!Directory.Exists(wanted)) return $"Error: 目录不存在：{wanted}";

        // 会话工作目录得是 git 仓库，才有"同一仓库"可比；不是 git 仓库时，worktree 就是用户自己准备的
        // 隔离副本——没有仓库身份可校验，边界是「用户显式指向 + 审批卡确认」这一下
        WorktreeGitResult wsCommon = await _git.RunAsync(workspaceFull, ["rev-parse", "--git-common-dir"],
            cancellationToken).ConfigureAwait(false);
        if (!wsCommon.Succeeded)
        {
            session.AddSessionApprovedWorktreeRoot(wanted);
            return $"已登记 worktree（工作目录不是 git 仓库，未做同仓库校验）：{wanted}\n" +
                   "这是你自己准备的隔离副本；登记后 worktree 内的文件写入免审批（AutoEdit/FullAuto 档）。\n" +
                   $"注意：本会话的工作目录（相对路径基准）没有变，仍是 {workspaceFull}；" +
                   "在 worktree 里干活请用绝对路径。草稿目录 $DRAFT 与记忆目录 $MEMORY 都不变。\n" +
                   "用 worktree.status 查询状态，用 worktree.reset 动态切回，用 worktree.remove 清理磁盘。";
        }

        // 目标是 git 工作树：show-toplevel 会把它归到树根（用户给子目录也按整棵树登记）
        WorktreeGitResult toplevel = await _git.RunAsync(wanted, ["rev-parse", "--show-toplevel"],
            cancellationToken).ConfigureAwait(false);
        if (!toplevel.Succeeded) return $"Error: {wanted} 不是 git 工作树：{Brief(toplevel)}";
        string root = toplevel.Output.Trim();

        WorktreeGitResult targetCommon = await _git.RunAsync(root, ["rev-parse", "--git-common-dir"],
            cancellationToken).ConfigureAwait(false);
        if (!targetCommon.Succeeded) return $"Error: 读 {root} 的公共 git 目录失败：{Brief(targetCommon)}";

        // 链接 worktree 的 git-common-dir 是 <主库>/.git/worktrees/<name>，公共根是 <主库>/.git。
        // 会话工作区本身是链接 worktree 也一样：两棵兄弟树的公共根相等，「同一仓库」成立
        string wsCommonRoot = CommonRootOf(NormalizeGitDir(workspaceFull, wsCommon.Output.Trim()));
        string targetCommonRoot = CommonRootOf(NormalizeGitDir(root, targetCommon.Output.Trim()));
        if (!RootComparer.Equals(wsCommonRoot, targetCommonRoot))
        {
            return $"Error: {root} 不是本仓库（{workspaceFull}）的 worktree，不能登记。" +
                   "它属于另一个 git 仓库；要换工作目录请用界面改绑。";
        }

        session.AddSessionApprovedWorktreeRoot(root);
        return $"已登记 worktree：{root}\n" +
               "登记后 worktree 内的文件写入免审批（AutoEdit/FullAuto 档），子代理派活时自动继承。\n" +
               $"注意：本会话的工作目录（相对路径基准）没有变，仍是 {workspaceFull}；" +
               "在 worktree 里干活请用绝对路径。草稿目录 $DRAFT 与记忆目录 $MEMORY 都不变。\n" +
               "用 worktree.status 查询状态，用 worktree.reset 动态切回，用 worktree.remove 清理磁盘。";
    }

    /// <summary>
    /// 清理（删除）一个已登记的 worktree：git worktree remove + 取消登记。
    /// 有未提交改动时 git 会拒删，默认不强制；确认后传 force
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="path">要删除的 worktree 根</param>
    /// <param name="force">是否强制删除（丢弃未提交改动）；默认 false</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>给模型的文本</returns>
    public async Task<string> RemoveAsync(ChatSession session, string? path, bool force,
        CancellationToken cancellationToken)
    {
        string workspace = WorkspaceOf(session)?.Trim() ?? string.Empty;
        if (workspace.Length == 0) return "Error: 会话没有绑定工作目录。";
        if (string.IsNullOrWhiteSpace(path)) return "Error: path 必填（要删除的 worktree 根）。";

        string workspaceFull = Full(workspace, "工作目录");
        if (workspaceFull.Length == 0) return "Error: 工作目录路径不成形。";
        string wanted = Full(path.Trim(), "path", workspaceFull);
        if (wanted.Length == 0) return "Error: path 路径不成形。";

        string? root = session.SnapshotSessionApprovedWorktreeRoots().FirstOrDefault(x =>
            RootComparer.Equals(x.Root, wanted.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))?.Root;
        if (root == null) return $"Error: 没有登记过 {wanted}。用 worktree.list 查看当前登记的 worktree。";

        // 目录已不在（可能被外部删了）：只取消登记 + prune 一下登记表
        if (!Directory.Exists(root))
        {
            session.RemoveSessionApprovedWorktreeRoot(root);
            _ = await _git.RunAsync(workspaceFull, ["worktree", "prune"], cancellationToken).ConfigureAwait(false);
            return $"目录已不存在，已取消登记：{root}（git worktree prune 已清理）。";
        }

        // 目标是 git 工作树 → git worktree remove（脏树默认拒删）；纯副本 → 直接删目录
        WorktreeGitResult isGitTree = await _git.RunAsync(root, ["rev-parse", "--git-common-dir"], cancellationToken)
            .ConfigureAwait(false);
        if (isGitTree.Succeeded)
        {
            List<string> args = ["worktree", "remove"];
            if (force) args.Add("--force");
            args.Add(root);
            WorktreeGitResult removed = await _git.RunAsync(workspaceFull, args, cancellationToken).ConfigureAwait(false);
            if (!removed.Succeeded)
            {
                return $"Error: 删除 worktree 失败：{Brief(removed)}" +
                       (force
                           ? string.Empty
                           : "（可能有未提交改动；确认丢弃后可用 force=true 重试，或先处理改动。）");
            }
        }
        else
        {
            // 纯副本：非空目录默认拒删（与 git 的脏树拒删同一语义）；force=true 才递归删
            if (!force && Directory.EnumerateFileSystemEntries(root).Any())
            {
                return $"Error: 删除失败：{root} 非空（可能有改动）；确认清理后传 force=true。";
            }

            Directory.Delete(root, recursive: true);
        }

        session.RemoveSessionApprovedWorktreeRoot(root);
        return $"已删除并取消登记 worktree：{root}。";
    }

    /// <summary>
    /// 列出已登记的 worktree：分支、上次提交时间（无提交时回落到目录改动时间）、有无未提交改动
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>给模型的文本</returns>
    public async Task<string> ListAsync(ChatSession session, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> roots = session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root).ToList();
        if (roots.Count == 0) return "没有登记的 worktree。";

        StringBuilder sb = new($"登记的 worktree（{roots.Count} 个）：\n");
        foreach (string root in roots)
        {
            WorktreeGitResult branch = await _git.RunAsync(root, ["branch", "--show-current"], cancellationToken)
                .ConfigureAwait(false);
            string branchName = branch.Succeeded ? branch.Output.Trim() : "—";

            WorktreeGitResult lastCommit = await _git.RunAsync(root, ["log", "-1", "--format=%cI"], cancellationToken)
                .ConfigureAwait(false);
            string modified = lastCommit.Succeeded && lastCommit.Output.Trim().Length > 0
                ? lastCommit.Output.Trim()
                : Directory.Exists(root) ? Directory.GetLastWriteTimeUtc(root).ToString("u") : "目录已不存在";

            WorktreeGitResult status = await _git.RunAsync(root, ["status", "--porcelain"], cancellationToken)
                .ConfigureAwait(false);
            // 非 git 副本没有状态可查：显示「—」而不是假装是干净的
            string dirty = status.Succeeded
                ? (status.Output.Trim().Length > 0 ? "有未提交改动" : "干净")
                : "—";

            sb.AppendLine($"- {root}");
            sb.AppendLine($"    分支：{branchName}；上次修改：{modified}；{dirty}");
        }

        sb.AppendLine("清理用 worktree.remove <path>；切回主工作目录用 worktree.reset。");
        return sb.ToString();
    }

    /// <summary>
    /// 取消登记（动态切回）：只取消指定 path，或全部。
    /// 只撤豁免、不动磁盘；切回后提示清理，让模型决定是否删目录
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="path">要取消的 worktree 根；空则全部取消</param>
    /// <returns>给模型的文本</returns>
    public static string Reset(ChatSession session, string? path)
    {
        if (path is { Length: > 0 } && !string.IsNullOrWhiteSpace(path))
        {
            string wanted = Full(path.Trim(), "path", WorkspaceOf(session));
            if (wanted.Length == 0) return "Error: path 路径不成形。";

            bool existed = session.SnapshotSessionApprovedWorktreeRoots().Any(x =>
                RootComparer.Equals(x.Root, wanted.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            if (!existed) return $"Error: 没有登记过 {wanted}。用 worktree.status 查询当前登记。";

            session.RemoveSessionApprovedWorktreeRoot(wanted);
            return $"已取消登记 worktree：{wanted}。恢复为仅工作目录内免审批。\n" +
                   $"提示：{wanted} 目录还在磁盘上；不用了就 worktree.remove {wanted} 清理。";
        }

        int count = session.SnapshotSessionApprovedWorktreeRoots().Count;
        if (count == 0) return "当前没有登记的 worktree。";

        string cleared = string.Join("、", session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
        session.ClearSessionApprovedWorktreeRoots();
        session.SaveMeta(); // Clear 不落盘，这里补
        return $"已取消全部 {count} 个 worktree 登记，恢复为仅工作目录内免审批。\n" +
               $"提示：目录仍在磁盘上（{cleared}）；不用了就 worktree.remove 清理。";
    }

    /// <summary>
    /// 当前状态：工作目录、已登记的 worktree、草稿与记忆目录
    /// </summary>
    /// <param name="session">会话</param>
    /// <returns>给模型的文本</returns>
    public static string Status(ChatSession session)
    {
        AgentPathResolver paths = AgentBuildProfile.PathResolverOf(session);
        IReadOnlyList<string> roots = session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root).ToList();
        string? workspace = WorkspaceOf(session);

        StringBuilder sb = new();
        sb.AppendLine($"会话工作目录（相对路径基准）：{(workspace?.Length > 0 ? workspace : "（未绑定）")}");
        sb.Append(roots.Count == 0
            ? "登记的 worktree（免审批）：无\n"
            : "登记的 worktree（免审批）：\n  " + string.Join("\n  ", roots) + "\n");
        if (paths.DraftRoot.Length > 0) sb.AppendLine($"草稿目录 $DRAFT：{paths.DraftRoot}");
        if (paths.MemoryRoot.Length > 0) sb.AppendLine($"记忆目录 $MEMORY：{paths.MemoryRoot}");
        sb.AppendLine("说明：登记的 worktree 内文件写入免审批；工作目录本身未变，worktree 内干活请用绝对路径；草稿与记忆不变。");
        return sb.ToString();
    }

    /// <summary>缺省落盘位置：数据目录里项目共享的 Worktrees 下（跟 memory 同家，跨会话共享）</summary>
    private static string DefaultDirectory(string workspaceFull, string name) =>
        Path.Combine(WorktreeLayout.GetWorktreesRoot(workspaceFull), name);

    /// <summary>绝对化；相对路径按 <paramref name="baseDirectory"/> 拼（缺省按进程 cwd——worktree 参数一律给 baseDirectory，
    /// 别让相对路径落到应用 cwd）。失败返回空串（报错话术在调用方）</summary>
    private static string Full(string path, string label, string? baseDirectory = null)
    {
        try
        {
            return Path.GetFullPath(string.IsNullOrWhiteSpace(baseDirectory) ? path : Path.Combine(baseDirectory, path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>git rev-parse 输出的公共 git 目录可能相对（主工作树给 ".git"）；统一绝对化再比</summary>
    private static string NormalizeGitDir(string workingDirectory, string output)
    {
        if (output.Length == 0) return string.Empty;
        string full = Path.IsPathRooted(output)
            ? Path.GetFullPath(output)
            : Path.GetFullPath(Path.Combine(workingDirectory, output));
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static StringComparer RootComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static StringComparison RootComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>公共 git 目录归到仓库根：主工作区是 <c>&lt;repo&gt;/.git</c>，链接 worktree 是
    /// <c>&lt;repo&gt;/.git/worktrees/&lt;name&gt;</c>——逐级上溯到段名为 <c>.git</c> 的那一级</summary>
    private static string CommonRootOf(string commonDirFull)
    {
        string current = commonDirFull;
        while (true)
        {
            string? parent = Path.GetDirectoryName(current);
            if (parent == null) return commonDirFull;
            if (Path.GetFileName(current).Equals(".git", RootComparison)) return current;
            current = parent;
        }
    }

    /// <summary>失败消息截短给模型看，别把整屏堆栈塞进上下文</summary>
    private static string Brief(WorktreeGitResult result)
    {
        string text = result.Error.Length > 0 ? result.Error : result.Output;
        text = text.Trim();
        return text.Length <= 200 ? text : text[..200] + "…";
    }
}
