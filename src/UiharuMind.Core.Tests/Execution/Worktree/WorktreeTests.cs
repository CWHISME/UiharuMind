using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution.Tools.Worktree;
using UiharuMind.Core.Tests.Agent;

namespace UiharuMind.Core.Tests.Execution.Worktree;

/// <summary>
/// worktree 扩展：登记进会话、审批动态豁免（切过去/切回来都不重建装配）、
/// create/set/reset/status 的命令序列与报错。git 一律用假执行器，不碰网络与真实仓库
/// </summary>
public class WorktreeTests
{
    private const string Workspace = "/tmp/uiharu-ws";

    private static ChatSession NewSession() => new() { IsTransient = true, WorkspacePath = Workspace };

    private static FunctionCallContent Edit(string filePath) => new("c1", "Edit",
        new Dictionary<string, object?> { ["filePath"] = filePath });

    private sealed class FakeGit : IWorktreeGitRunner
    {
        public List<string> Commands { get; } = [];

        /// <summary>按工作目录与前两个词分发；没写到的命令一律成功</summary>
        public Func<string, IReadOnlyList<string>, WorktreeGitResult?>? Handler { get; set; }

        public Task<WorktreeGitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args,
            CancellationToken cancellationToken)
        {
            Commands.Add(string.Join(" ", args));
            WorktreeGitResult? result = Handler?.Invoke(workingDirectory, args);
            return Task.FromResult(result ?? new WorktreeGitResult(string.Empty, string.Empty, 0));
        }
    }

    //================= 会话登记 =================

    [Fact]
    public void Session_AddRemoveClear_NormalizeAndDedupe()
    {
        ChatSession session = NewSession();

        session.AddSessionApprovedWorktreeRoot("/tmp/wt/a");
        session.AddSessionApprovedWorktreeRoot("/tmp/wt/a/"); // 尾分隔符归一，去重
        Assert.Equal([Path.GetFullPath("/tmp/wt/a")],
            session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));

        session.AddSessionApprovedWorktreeRoot("/tmp/wt/b");
        Assert.Equal(2, session.SnapshotSessionApprovedWorktreeRoots().Count);

        session.RemoveSessionApprovedWorktreeRoot("/tmp/wt/a");
        Assert.Equal([Path.GetFullPath("/tmp/wt/b")],
            session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));

        session.ClearSessionApprovedWorktreeRoots();
        Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
    }

    /// <summary>登记自动带上当前有效工作区（群成员跟群走）；非群成员就是自己绑的</summary>
    [Fact]
    public void Session_AddRecordsTheEffectiveWorkspace()
    {
        ChatSession session = NewSession();

        session.AddSessionApprovedWorktreeRoot("/tmp/wt/a");

        ChatSession.SessionWorktreeGrant grant = Assert.Single(session.SnapshotSessionApprovedWorktreeRoots());
        Assert.Equal(Path.GetFullPath("/tmp/wt/a"), grant.Root);
        Assert.Equal(Path.GetFullPath(Workspace), grant.Workspace);
    }

    //================= create =================

    /// <summary>数据目录项目共享 Worktrees 根下的目标（create 的 directory 必须落在根内；缺省也落这里）</summary>
    private static string CreateTarget(params string[] parts) =>
        Path.GetFullPath(Path.Combine(WorktreeLayout.GetWorktreesRoot(Path.GetFullPath(Workspace)), Path.Combine(parts)));

    [Fact]
    public async Task Create_RunsFetchThenAdd_RegistersTheWorktree()
    {
        ChatSession session = NewSession();
        // 同名分支本地与 origin/ 都没有：走 -b 新建（基于 HEAD）
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["rev-parse", "--verify", _]
                ? new WorktreeGitResult(string.Empty, "unknown", 128)
                : null,
        };
        string target = CreateTarget("feature", "x");

        string result = await new WorktreeGitService(git).CreateAsync(session, "feature/x", null, null,
            CreateTarget("feature", "x"), TestContext.Current.CancellationToken);

        Assert.Contains("已创建并登记", result);
        Assert.Contains("$WORKTREES/feature/x", result); // 落点在根下：给简写，不逼模型抄带哈希的长路径
        Assert.Contains("$WORKTREES", result);
        Assert.Contains(target, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
        Assert.Contains("fetch origin --prune --tags", git.Commands);
        Assert.Contains("submodule update --init --recursive", git.Commands);
        Assert.Contains("ls-files -s", git.Commands); // 软链接探测
        Assert.Contains(git.Commands, c => c.Contains($"worktree add -b feature/x {target} HEAD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_ExistingBranch_AttachesWithoutCreating()
    {
        ChatSession session = NewSession();
        FakeGit git = new(); // 一切成功：refs/heads/exists 存在
        string target = CreateTarget("exists");

        string result = await new WorktreeGitService(git).CreateAsync(session, "exists", null, null,
            CreateTarget("exists"), TestContext.Current.CancellationToken);

        Assert.Contains("已存在同名分支", result);
        Assert.Contains(git.Commands, c => c.Contains($"worktree add {target} exists", StringComparison.Ordinal));
        Assert.DoesNotContain(git.Commands, c => c.Contains("worktree add -b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_LocalMissing_OriginPresent_TracksOrigin()
    {
        ChatSession session = NewSession();
        string target = CreateTarget("fix");
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["rev-parse", "--verify", "refs/heads/fix"]
                ? new WorktreeGitResult(string.Empty, "unknown", 128)
                : null,
        };

        string result = await new WorktreeGitService(git).CreateAsync(session, "fix", null, null,
            CreateTarget("fix"), TestContext.Current.CancellationToken);

        Assert.Contains("跟踪 origin/fix", result);
        Assert.Contains(git.Commands, c => c.Contains($"worktree add -b fix {target} origin/fix", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_BaseOnlyOnOrigin_UsesOriginBranch()
    {
        ChatSession session = NewSession();
        // base 只在 origin/：本地 main、同名分支本地与 origin/ 都不存在
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["rev-parse", "--verify", "main"]
                                        or ["rev-parse", "--verify", "refs/heads/fix"]
                                        or ["rev-parse", "--verify", "origin/fix"]
                ? new WorktreeGitResult(string.Empty, "unknown", 128)
                : null,
        };
        string target = CreateTarget("fix");

        string result = await new WorktreeGitService(git).CreateAsync(session, "fix", "main", null,
            CreateTarget("fix"), TestContext.Current.CancellationToken);

        Assert.Contains("origin/main", result);
        Assert.Contains(git.Commands, c => c.Contains($"worktree add -b fix {target} origin/main", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_BaseNowhere_ReturnsError_AndDoesNotRegister()
    {
        ChatSession session = NewSession();
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["rev-parse", "--verify", _]
                ? new WorktreeGitResult(string.Empty, "unknown", 128)
                : null,
        };

        string result = await new WorktreeGitService(git).CreateAsync(session, "fix", "nope", null, null,
            TestContext.Current.CancellationToken);

        Assert.Contains("找不到 base 'nope'", result);
        Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
    }

    [Fact]
    public async Task Create_TargetAlreadyExists_ReturnsError()
    {
        ChatSession session = NewSession();
        FakeGit git = new();
        string existing = CreateTarget("taken");
        Directory.CreateDirectory(existing);
        try
        {
            File.WriteAllText(Path.Combine(existing, "x.txt"), "x");

            string result = await new WorktreeGitService(git).CreateAsync(session, "taken", null, null,
                CreateTarget("taken"), TestContext.Current.CancellationToken);

            Assert.Contains("已存在且非空", result);
            Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
        }
        finally
        {
            Directory.Delete(existing, recursive: true);
        }
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsError()
    {
        string result = await new WorktreeGitService(new FakeGit()).CreateAsync(NewSession(), "  ", null, null, null,
            TestContext.Current.CancellationToken);

        Assert.Contains("name 必填", result);
    }

    /// <summary>directory 给任意绝对路径也能建并登记：git 场景建出来的树天然同仓库、纯副本是工作目录内容的拷贝，
    /// 不需要锁在 Worktrees 根内（真正的守卫是目标必须为空/不存在）</summary>
    [Fact]
    public async Task Create_OutsideWorktreesRoot_IsAllowedAndRegistered()
    {
        ChatSession session = NewSession();
        string outside = Path.GetFullPath("/tmp/not-in-worktrees/feature/x");

        string result = await new WorktreeGitService(new FakeGit()).CreateAsync(session, "feature/x", null, null,
            outside, TestContext.Current.CancellationToken);

        Assert.Contains("已创建并登记", result);
        Assert.Contains(outside, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
    }

    /// <summary>工作目录不是 git 仓库也能建：复制成隔离副本并登记（git 只是有则锦上添花，不是前提）。
    /// 常见大目录（node_modules 等）跳过</summary>
    [Fact]
    public async Task Create_NoGit_CopiesWorkspaceAndRegisters()
    {
        string src = Path.Combine(Path.GetTempPath(), "wrk-no-git-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(src);
        string target = Path.Combine(WorktreeLayout.GetWorktreesRoot(src), "copy");
        try
        {
            File.WriteAllText(Path.Combine(src, "a.txt"), "x");
            Directory.CreateDirectory(Path.Combine(src, "node_modules"));
            File.WriteAllText(Path.Combine(src, "node_modules", "big.js"), "big");
            ChatSession session = new() { IsTransient = true, WorkspacePath = src };
            FakeGit git = new()
            {
                Handler = (_, args) => args is ["rev-parse", "--git-common-dir"]
                    ? new WorktreeGitResult(string.Empty, "not a git repository", 128)
                    : null,
            };

            string result = await new WorktreeGitService(git).CreateAsync(session, "copy", null, null, null,
                TestContext.Current.CancellationToken);

            Assert.Contains("纯副本", result);
            Assert.Contains(target, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
            Assert.True(File.Exists(Path.Combine(target, "a.txt")), "内容应被复制");
            Assert.False(Directory.Exists(Path.Combine(target, "node_modules")), "node_modules 应被排除");
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            if (Directory.Exists(src)) Directory.Delete(src, recursive: true);
        }
    }

    /// <summary>工作目录不是 git 仓库时 set 也能登记自己准备的隔离副本（没有仓库身份可比，不再硬拒）</summary>
    [Fact]
    public async Task Set_NoGit_AcceptsExistingDirectory()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "wrk-no-git-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workspace);
        string copy = Path.Combine(Path.GetTempPath(), "wk-copy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(copy);
        try
        {
            ChatSession session = new() { IsTransient = true, WorkspacePath = workspace };
            FakeGit git = new()
            {
                Handler = (_, args) => args is ["rev-parse", "--git-common-dir"]
                    ? new WorktreeGitResult(string.Empty, "not a git repository", 128)
                    : null,
            };

            string result = await new WorktreeGitService(git).SetAsync(session, copy, TestContext.Current.CancellationToken);

            Assert.Contains("已登记", result);
            Assert.Contains("未做同仓库校验", result);
            Assert.Contains(copy, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
        }
        finally
        {
            if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>纯副本的 remove 走目录删除：非空默认拒删（同 git 脏树语义），force=true 才递归删</summary>
    [Fact]
    public async Task Remove_PlainCopy_DeletesDirectory()
    {
        ChatSession session = NewSession();
        string copy = Path.Combine(Path.GetTempPath(), "wk-copy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(copy);
        File.WriteAllText(Path.Combine(copy, "x.txt"), "x");
        session.AddSessionApprovedWorktreeRoot(copy);
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["rev-parse", "--git-common-dir"]
                ? new WorktreeGitResult(string.Empty, "not a git repository", 128)
                : null,
        };
        try
        {
            string refused = await new WorktreeGitService(git).RemoveAsync(session, copy, force: false,
                TestContext.Current.CancellationToken);
            Assert.Contains("非空", refused); // 有内容默认拒删
            Assert.Contains(copy, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));

            string removed = await new WorktreeGitService(git).RemoveAsync(session, copy, force: true,
                TestContext.Current.CancellationToken);
            Assert.Contains("已删除并取消登记", removed);
            Assert.False(Directory.Exists(copy));
            Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
        }
        finally
        {
            if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
        }
    }

    /// <summary>相对 directory 按工作目录解析（directory: "feature/foo" → &lt;工作目录&gt;/feature/foo），不用拼绝对路径</summary>
    [Fact]
    public async Task Create_RelativeDirectory_ResolvesUnderWorkspace()
    {
        ChatSession session = NewSession();
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["rev-parse", "--verify", _]
                ? new WorktreeGitResult(string.Empty, "unknown", 128)
                : null,
        };
        string target = Path.GetFullPath(Path.Combine(Workspace, "feature", "foo"));

        string result = await new WorktreeGitService(git).CreateAsync(session, "feature/foo", null, null,
            "feature/foo", TestContext.Current.CancellationToken);

        Assert.Contains(target, result);
        Assert.Contains(git.Commands, c => c.Contains($"worktree add -b feature/foo {target} HEAD", StringComparison.Ordinal));
        Assert.Contains(target, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
    }

    /// <summary>directory 可以给 $WORKTREES/… 简写：与文件工具同一条路径口径，省掉带哈希的长前缀</summary>
    [Fact]
    public async Task Create_ShorthandDirectory_ResolvesUnderTheWorktreesRoot()
    {
        ChatSession session = NewSession();
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["rev-parse", "--verify", _]
                ? new WorktreeGitResult(string.Empty, "unknown", 128)
                : null,
        };
        string target = CreateTarget("feature", "bar");

        string result = await new WorktreeGitService(git).CreateAsync(session, "feature/bar", null, null,
            "$WORKTREES/feature/bar", TestContext.Current.CancellationToken);

        Assert.Contains(git.Commands, c => c.Contains($"worktree add -b feature/bar {target} HEAD", StringComparison.Ordinal));
        Assert.Contains(target, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
        Assert.Contains("$WORKTREES/feature/bar", result);
    }

    /// <summary>缺省落盘：数据目录里项目共享的 Worktrees 下（跟 memory 同家）</summary>
    [Fact]
    public void Layout_WorktreesRoot_IsProjectSharedUnderAgentWorkspaces()
    {
        string root = WorktreeLayout.GetWorktreesRoot("/tmp/uiharu-ws");

        // <AgentWorkspaces>/<工作区段>/Worktrees —— 家目录同 WorkspaceSegment（与记忆/产物同段）
        Assert.Equal(WorkspaceSegment.From("/tmp/uiharu-ws"), Path.GetFileName(Path.GetDirectoryName(root)));
        Assert.Equal(WorktreeLayout.FolderName, Path.GetFileName(root));
        Assert.Equal(string.Empty, WorktreeLayout.GetWorktreesRoot(null));
        Assert.Equal(string.Empty, WorktreeLayout.GetWorktreesRoot("  "));
    }

    //================= set =================

    [Fact]
    public async Task Set_SameRepo_RegistersTheToplevel()
    {
        ChatSession session = NewSession();
        string workspaceFull = Path.GetFullPath(Workspace);
        string target = Path.GetFullPath("/tmp/uiharu-ws-worktrees/user-made");
        Directory.CreateDirectory(target);
        try
        {
            FakeGit git = new()
            {
                Handler = (dir, args) => args switch
                {
                    // 主工作区与链接 worktree 的公共 git 目录：链接的落在主库 .git 的 worktrees/ 下
                    ["rev-parse", "--git-common-dir"] when dir == workspaceFull =>
                        new WorktreeGitResult(".git", string.Empty, 0),
                    ["rev-parse", "--git-common-dir"] =>
                        new WorktreeGitResult(Path.Combine(workspaceFull, ".git", "worktrees", "user-made"), string.Empty, 0),
                    ["rev-parse", "--show-toplevel"] => new WorktreeGitResult(target, string.Empty, 0),
                    _ => null,
                },
            };

            string result = await new WorktreeGitService(git).SetAsync(session, target,
                TestContext.Current.CancellationToken);

            Assert.Contains("已登记", result);
            Assert.Contains(target, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    /// <summary>会话工作区本身是链接 worktree 时，同仓库的兄弟树也认（公共 git 目录归到仓库根再比）</summary>
    [Fact]
    public async Task Set_WorkspaceIsLinkWorktree_AcceptsSibling()
    {
        ChatSession session = NewSession();
        string workspaceFull = Path.GetFullPath(Workspace);
        string target = Path.GetFullPath("/tmp/uiharu-ws-worktrees/sibling");
        Directory.CreateDirectory(target);
        try
        {
            FakeGit git = new()
            {
                Handler = (dir, args) => args switch
                {
                    // 会话工作区本身是链接 worktree a：common dir 在 .git/worktrees/a
                    ["rev-parse", "--git-common-dir"] when dir == workspaceFull =>
                        new WorktreeGitResult(
                            Path.Combine(workspaceFull, ".git", "worktrees", "a"), string.Empty, 0),
                    // 目标是同仓库的兄弟链接 worktree b：两棵树的公共根都是 <repo>/.git
                    ["rev-parse", "--git-common-dir"] =>
                        new WorktreeGitResult(
                            Path.Combine(workspaceFull, ".git", "worktrees", "b"), string.Empty, 0),
                    ["rev-parse", "--show-toplevel"] => new WorktreeGitResult(target, string.Empty, 0),
                    _ => null,
                },
            };

            string result = await new WorktreeGitService(git).SetAsync(session, target,
                TestContext.Current.CancellationToken);

            Assert.Contains("已登记", result);
            Assert.Contains(target, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public async Task Set_DifferentRepo_Rejects()
    {
        ChatSession session = NewSession();
        string workspaceFull = Path.GetFullPath(Workspace);
        string target = Path.GetFullPath("/tmp/uiharu-ws-worktrees/other-repo");
        Directory.CreateDirectory(target);
        try
        {
            FakeGit git = new()
            {
                Handler = (dir, args) => args switch
                {
                    ["rev-parse", "--git-common-dir"] when dir == workspaceFull =>
                        new WorktreeGitResult(".git", string.Empty, 0),
                    // 别的仓库：公共 git 目录在别处（不在主库 .git 之下）
                    ["rev-parse", "--git-common-dir"] =>
                        new WorktreeGitResult(Path.Combine(target, ".git"), string.Empty, 0),
                    ["rev-parse", "--show-toplevel"] => new WorktreeGitResult(target, string.Empty, 0),
                    _ => null,
                },
            };

            string result = await new WorktreeGitService(git).SetAsync(session, target,
                TestContext.Current.CancellationToken);

            Assert.Contains("不是本仓库", result);
            Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public async Task Set_NotAGitTree_Rejects()
    {
        ChatSession session = NewSession();
        string workspaceFull = Path.GetFullPath(Workspace);
        string target = Path.GetFullPath("/tmp/uiharu-ws-worktrees/not-git");
        Directory.CreateDirectory(target);
        try
        {
            FakeGit git = new()
            {
                Handler = (dir, args) => dir == workspaceFull && args is ["rev-parse", "--git-common-dir"]
                    ? new WorktreeGitResult(".git", string.Empty, 0)
                    : args is ["rev-parse", "--show-toplevel"]
                        ? new WorktreeGitResult(string.Empty, "not a git repository", 128)
                        : null,
            };

            string result = await new WorktreeGitService(git).SetAsync(session, target,
                TestContext.Current.CancellationToken);

            Assert.Contains("不是 git 工作树", result);
            Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    //================= reset / status =================

    [Fact]
    public void Reset_RemovesOneThenAll_AndHintsCleanup()
    {
        ChatSession session = NewSession();
        session.AddSessionApprovedWorktreeRoot("/tmp/wt/a");
        session.AddSessionApprovedWorktreeRoot("/tmp/wt/b");

        string one = WorktreeGitService.Reset(session, "/tmp/wt/a");
        Assert.Contains("已取消登记", one);
        Assert.Contains("清理", one); //切回主工作目录时提示清理
        Assert.Equal([Path.GetFullPath("/tmp/wt/b")], session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root));

        string all = WorktreeGitService.Reset(session, null);
        Assert.Contains("已取消全部 1 个", all);
        Assert.Contains("worktree.remove", all);
        Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
    }

    [Fact]
    public void Status_ListsWorkspaceAndRegisteredRoots()
    {
        ChatSession session = NewSession();
        session.AddSessionApprovedWorktreeRoot("/tmp/wt/a");

        string text = WorktreeGitService.Status(session);

        Assert.Contains(Workspace, text);
        Assert.Contains(Path.GetFullPath("/tmp/wt/a"), text);
        Assert.Contains("$DRAFT", text);
        Assert.Contains("$MEMORY", text);
    }

    //================= remove / list =================

    [Fact]
    public async Task Remove_DeletesAndUnregisters()
    {
        ChatSession session = NewSession();
        string root = Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x");
        Directory.CreateDirectory(root);
        session.AddSessionApprovedWorktreeRoot(root);
        FakeGit git = new(); // git worktree remove 成功
        try
        {
            string result = await new WorktreeGitService(git).RemoveAsync(session, root, force: false,
                TestContext.Current.CancellationToken);

            Assert.Contains("已删除并取消登记", result);
            Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
            Assert.Contains($"worktree remove {root}", git.Commands);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Remove_Dirty_RefusesWithoutForce_AndKeepsRegistration()
    {
        ChatSession session = NewSession();
        string root = Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x");
        Directory.CreateDirectory(root);
        session.AddSessionApprovedWorktreeRoot(root);
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["worktree", "remove", _]
                ? new WorktreeGitResult(string.Empty, "contains modified or untracked files", 128)
                : null,
        };
        try
        {
            string result = await new WorktreeGitService(git).RemoveAsync(session, root, force: false,
                TestContext.Current.CancellationToken);

            Assert.Contains("删除 worktree 失败", result);
            Assert.Contains("force=true", result);
            Assert.Contains(root, session.SnapshotSessionApprovedWorktreeRoots().Select(x => x.Root)); // 没删成，登记保留
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Remove_DirectoryGone_JustUnregisters()
    {
        ChatSession session = NewSession();
        string root = Path.GetFullPath("/tmp/uiharu-ws-worktrees/gone");
        session.AddSessionApprovedWorktreeRoot(root); // 目录并不存在
        FakeGit git = new();

        string result = await new WorktreeGitService(git).RemoveAsync(session, root, force: false,
            TestContext.Current.CancellationToken);

        Assert.Contains("已取消登记", result);
        Assert.Contains(git.Commands, c => c.Contains("prune", StringComparison.Ordinal));
        Assert.Empty(session.SnapshotSessionApprovedWorktreeRoots());
    }

    [Fact]
    public async Task Remove_UnregisteredPath_ReturnsError()
    {
        string result = await new WorktreeGitService(new FakeGit()).RemoveAsync(NewSession(), "/tmp/wt/nope",
            force: false, TestContext.Current.CancellationToken);

        Assert.Contains("没有登记过", result);
    }

    [Fact]
    public async Task List_ShowsBranchLastModifiedAndDirtiness()
    {
        ChatSession session = NewSession();
        session.AddSessionApprovedWorktreeRoot("/tmp/wt/clean");
        session.AddSessionApprovedWorktreeRoot("/tmp/wt/dirty");
        FakeGit git = new()
        {
            Handler = (dir, args) => args switch
            {
                ["branch", "--show-current"] => new WorktreeGitResult(
                    dir.EndsWith("dirty") ? "fix/dirty" : "fix/clean", string.Empty, 0),
                ["log", "-1", "--format=%cI"] => new WorktreeGitResult("2026-01-05T08:00:00+00:00", string.Empty, 0),
                ["status", "--porcelain"] when dir.EndsWith("dirty") =>
                    new WorktreeGitResult(" M src/A.cs\n", string.Empty, 0),
                _ => null,
            },
        };

        string text = await new WorktreeGitService(git).ListAsync(session, TestContext.Current.CancellationToken);

        Assert.Contains("fix/clean", text);
        Assert.Contains("fix/dirty", text);
        Assert.Contains("2026-01-05T08:00:00+00:00", text);
        Assert.Contains("有未提交改动", text);
        Assert.Contains("干净", text);
    }

    /// <summary>list 输出落在 worktree 根下的登记转成 $WORKTREES/… 简写（模型照抄即可），根外照旧绝对路径</summary>
    [Fact]
    public async Task List_ShowsTheWorktreesShorthand_ForRootsUnderTheRoot()
    {
        ChatSession session = NewSession();
        string wtRoot = WorktreeLayout.GetWorktreesRoot(Workspace);
        session.AddSessionApprovedWorktreeRoot(Path.Combine(wtRoot, "feature", "x"));
        session.AddSessionApprovedWorktreeRoot(Path.Combine("/tmp", "wt", "outside"));
        FakeGit git = new()
        {
            Handler = (_, args) => args switch
            {
                ["branch", "--show-current"] => new WorktreeGitResult("fix/x", string.Empty, 0),
                ["log", "-1", "--format=%cI"] => new WorktreeGitResult("2026-01-05T08:00:00+00:00", string.Empty, 0),
                ["status", "--porcelain"] => new WorktreeGitResult(string.Empty, string.Empty, 0),
                _ => null,
            },
        };

        string text = await new WorktreeGitService(git).ListAsync(session, TestContext.Current.CancellationToken);

        Assert.Contains("$WORKTREES/feature/x", text);
        Assert.Contains(Path.GetFullPath("/tmp/wt/outside"), text);
    }

    /// <summary>status 同样把登记 root 转成 $WORKTREES/… 简写</summary>
    [Fact]
    public void Status_ShowsTheWorktreesShorthand_ForRootsUnderTheRoot()
    {
        ChatSession session = NewSession();
        string wtRoot = WorktreeLayout.GetWorktreesRoot(Workspace);
        session.AddSessionApprovedWorktreeRoot(Path.Combine(wtRoot, "feature", "x"));

        string text = WorktreeGitService.Status(session);

        Assert.Contains("$WORKTREES/feature/x", text);
    }

    /// <summary>提示词目录段教 $WORKTREES 简写并说明已注入环境变量——模型不知道有这个变量，就不会用它</summary>
    [Fact]
    public void ToolPrompt_TeachTheWorktreesShorthand_AndTheInjectedVariable()
    {
        string text = AgentToolPrompts.BuildDirectories(true, "/tmp/uiharu-ws", "$DRAFT", "$MEMORY", "$WORKTREES");

        Assert.Contains("- worktree 根 $WORKTREES", text);
        Assert.Contains("$WORKTREES/名字", text);
        Assert.Contains("已注入的环境变量", text);
        Assert.Contains("$WORKTREES", text);
    }

    //================= 子模块：本地复制 + 网络兜底 + 建分支 =================

    /// <summary>主工作区已检出的子模块：网络拉取失败时直接从主工作区本地 clone（保留 git 身份），
    /// 再建同名分支。命令序列 clone → absorbgitdirs → update 兜底 → switch -c；结果里这三步都要交代</summary>
    [Fact]
    public async Task Create_Submodule_NetworkFails_ClonesFromMain_AndBranches()
    {
        string ws = Path.Combine(Path.GetTempPath(), "wrk-sub-" + Guid.NewGuid().ToString("N")[..8]);
        string sub = Path.Combine(ws, "src", "Sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, ".git"), "gitdir: fake");
        File.WriteAllText(Path.Combine(sub, "sub.txt"), "x");
        string target = Path.Combine(WorktreeLayout.GetWorktreesRoot(ws), "feature", "x");
        try
        {
            ChatSession session = new() { IsTransient = true, WorkspacePath = ws };
            FakeGit git = new()
            {
                Handler = (_, args) =>
                {
                    // 模拟真实 clone：把目录建出来、放 .git 标记，后面的建分支步骤才有 git 身份可查
                    if (args is ["clone", "--quiet", _, string dst])
                    {
                        Directory.CreateDirectory(dst);
                        File.WriteAllText(Path.Combine(dst, ".git"), "gitdir: fake");
                        return new WorktreeGitResult(string.Empty, string.Empty, 0);
                    }

                    return args switch
                    {
                        ["config", "-z", "--file", ".gitmodules", "--get-regexp", _] =>
                            new WorktreeGitResult("submodule.src/Sub.path\u0000src/Sub\u0000", string.Empty, 0),
                        ["submodule", "update", "--init", "--recursive"] =>
                            new WorktreeGitResult(string.Empty, "connect to host failed", 128),
                        ["submodule", "absorbgitdirs"] => new WorktreeGitResult(string.Empty, string.Empty, 0),
                        ["rev-parse", "--verify", "refs/heads/feature/x"] =>
                            new WorktreeGitResult(string.Empty, "unknown", 128),
                        ["rev-parse", "--verify", "origin/feature/x"] =>
                            new WorktreeGitResult(string.Empty, "unknown", 128),
                        ["rev-parse", "--verify", "HEAD"] => new WorktreeGitResult("aaa111", string.Empty, 0),
                        ["switch", "-c", "feature/x"] => new WorktreeGitResult(string.Empty, string.Empty, 0),
                        _ => null,
                    };
                },
            };

            string result = await new WorktreeGitService(git).CreateAsync(session, "feature/x", null, null,
                target, TestContext.Current.CancellationToken);

            Assert.Contains("已从主工作区本地复制", result);
            Assert.Contains("子模块已检出分支", result);
            Assert.Contains(git.Commands, c => c.StartsWith("clone --quiet", StringComparison.Ordinal));
            Assert.Contains("submodule absorbgitdirs", git.Commands);
            Assert.Contains(git.Commands, c => c.StartsWith("switch -c feature/x", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            if (Directory.Exists(ws)) Directory.Delete(ws, recursive: true);
        }
    }

    /// <summary>主工作区没检出该子模块：不做本地 clone，交给网络 update 兜底（命令里不应有 clone）</summary>
    [Fact]
    public async Task Create_Submodule_NotInMain_ReliesOnNetwork()
    {
        string ws = Path.Combine(Path.GetTempPath(), "wrk-sub-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ws);
        string target = Path.Combine(WorktreeLayout.GetWorktreesRoot(ws), "feature", "x");
        try
        {
            ChatSession session = new() { IsTransient = true, WorkspacePath = ws };
            FakeGit git = new()
            {
                Handler = (_, args) => args switch
                {
                    ["config", "-z", "--file", ".gitmodules", "--get-regexp", _] =>
                        new WorktreeGitResult("submodule.src/Sub.path\u0000src/Sub\u0000", string.Empty, 0),
                    ["rev-parse", "--verify", "refs/heads/feature/x"] =>
                        new WorktreeGitResult(string.Empty, "unknown", 128),
                    ["rev-parse", "--verify", "origin/feature/x"] =>
                        new WorktreeGitResult(string.Empty, "unknown", 128),
                    _ => null,
                },
            };

            string result = await new WorktreeGitService(git).CreateAsync(session, "feature/x", null, null,
                target, TestContext.Current.CancellationToken);

            Assert.Contains("子模块已就绪", result); // update 兜底成功
            Assert.DoesNotContain(git.Commands, c => c.StartsWith("clone", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            if (Directory.Exists(ws)) Directory.Delete(ws, recursive: true);
        }
    }

    /// <summary>子模块里已有同名分支且与当前 commit 一致：直接 switch 而不新建（-c）；不一致则不强切（避免弄脏父仓库）</summary>
    [Fact]
    public async Task Create_Submodule_ExistingBranchSameCommit_Switches_NotCreates()
    {
        string ws = Path.Combine(Path.GetTempPath(), "wrk-sub-" + Guid.NewGuid().ToString("N")[..8]);
        string sub = Path.Combine(ws, "src", "Sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, ".git"), "gitdir: fake");
        string target = Path.Combine(WorktreeLayout.GetWorktreesRoot(ws), "branchme");
        try
        {
            ChatSession session = new() { IsTransient = true, WorkspacePath = ws };
            // 让 update 命令模拟“联网拉成功并建出子模块目录”，后面的建分支步骤才有 git 身份可查
            FakeGit git = new()
            {
                Handler = (dir, args) =>
                {
                    if (args is ["submodule", "update", "--init", "--recursive"])
                    {
                        string subDir = Path.Combine(dir, "src", "Sub");
                        Directory.CreateDirectory(subDir);
                        File.WriteAllText(Path.Combine(subDir, ".git"), "gitdir: fake");
                        return new WorktreeGitResult(string.Empty, string.Empty, 0);
                    }

                    return args switch
                    {
                        ["config", "-z", "--file", ".gitmodules", "--get-regexp", _] =>
                            new WorktreeGitResult("submodule.src/Sub.path\u0000src/Sub\u0000", string.Empty, 0),
                        ["rev-parse", "--verify", "HEAD"] => new WorktreeGitResult("aaa111", string.Empty, 0),
                        ["rev-parse", "--verify", "refs/heads/branchme"] =>
                            new WorktreeGitResult("aaa111", string.Empty, 0), // 同 commit
                        ["switch", "branchme"] => new WorktreeGitResult(string.Empty, string.Empty, 0),
                        _ => null,
                    };
                },
            };

            string result = await new WorktreeGitService(git).CreateAsync(session, "branchme", null, null,
                target, TestContext.Current.CancellationToken);

            Assert.Contains("子模块已检出分支", result);
            Assert.Contains("switch branchme", git.Commands);
            Assert.DoesNotContain(git.Commands, c => c.StartsWith("switch -c", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            if (Directory.Exists(ws)) Directory.Delete(ws, recursive: true);
        }
    }

    //================= 软链接：平台能力 + worktree 级修复 =================

    /// <summary>平台支持符号链接：只对这张 worktree 开 core.symlinks（worktreeConfig），重新落盘；
    /// 命令里不能出现仓库级 config core.symlinks（那会改到主工作区）</summary>
    [Fact]
    public async Task Create_SymlinkSupported_RepairsWorktreeScoped()
    {
        ChatSession session = NewSession();
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["ls-files", "-s"]
                ? new WorktreeGitResult("120000 8ab6d0 0\tlink.txt\n100644 cf8 0\treadme.md", string.Empty, 0)
                : null,
        };
        string target = CreateTarget("sym");

        string result = await new WorktreeGitService(git, () => true).CreateAsync(session, "sym", null, null,
            target, TestContext.Current.CancellationToken);

        Assert.Contains("软链接已修复", result);
        Assert.Contains("config extensions.worktreeConfig true", git.Commands);
        Assert.Contains("config --worktree core.symlinks true", git.Commands);
        Assert.Contains("checkout-index -f -a", git.Commands);
        // 平台支持时 add 保持原行为（不带 -c）：链接直接按 core.symlinks 检出，随后由上面几步确认
        Assert.DoesNotContain(git.Commands,
            c => c.StartsWith("-c core.symlinks=false worktree add", StringComparison.Ordinal));
        Assert.DoesNotContain(git.Commands, c => c == "config core.symlinks true");
    }

    /// <summary>平台不支持符号链接（Windows 无开发者模式/管理员）：不强修，保持普通文件，也不碰任何 core.symlinks 配置</summary>
    [Fact]
    public async Task Create_SymlinkUnsupported_KeepsPlainFilesAndReports()
    {
        ChatSession session = NewSession();
        FakeGit git = new()
        {
            Handler = (_, args) => args is ["ls-files", "-s"]
                ? new WorktreeGitResult("120000 8ab6d0 0\tlink.txt\n", string.Empty, 0)
                : null,
        };
        string target = CreateTarget("sym");

        string result = await new WorktreeGitService(git, () => false).CreateAsync(session, "sym", null, null,
            target, TestContext.Current.CancellationToken);

        Assert.Contains("不能创建真符号链接", result);
        // add 带命令级 -c core.symlinks=false：符号链接按普通文件检出，不依赖建链接权限
        Assert.Contains(git.Commands,
            c => c.StartsWith("-c core.symlinks=false worktree add", StringComparison.Ordinal));
        Assert.DoesNotContain(git.Commands, c => c.Contains("checkout-index", StringComparison.Ordinal));
        // 不写任何持久配置：-c 只对该命令生效，主仓库的 core.symlinks 不被碰
        Assert.DoesNotContain(git.Commands,
            c => c.Contains("config", StringComparison.Ordinal) && c.Contains("core.symlinks", StringComparison.Ordinal));
    }

    /// <summary>ls-files -s 只挑 120000 条目，普通文件不混进来</summary>
    [Fact]
    public void SymlinkPaths_PicksOnlyThe120000Entries()
    {
        string output = "120000 8ab6d0 0\tlink.txt\n100644 cf8 0\treadme.md\n120000 abc 0\tdir/art\n";

        Assert.Equal(["link.txt", "dir/art"], WorktreeGitService.SymlinkPathsOf(output));
    }

    /// <summary>平台建不了真符号链接时，指向目录的条目重建为 junction；文件类/解析不了的保持普通文件</summary>
    [Fact]
    public async Task UnsupportedPlatform_MaterializesDirectoryLinks_AsJunctions()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wt-junc-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string real = Path.Combine(dir, "real");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "x.txt"), "x");
            string art = Path.Combine(dir, "Art");
            File.WriteAllText(art, real); //普通文件形态的链接：内容是目标路径
            string rel = Path.Combine(dir, "rel-link");
            File.WriteAllText(rel, "real"); //相对目标：按链接所在目录解析到 dir/real
            File.WriteAllText(Path.Combine(dir, "plain.txt"), "file content"); //文件类链接：不 junction
            string missing = Path.Combine(dir, "missing");
            File.WriteAllText(missing, Path.Combine(dir, "no-such-dir")); //目标不存在：跳过

            List<(string Link, string Target)> junctions = [];
            WorktreeGitService service = new(new FakeGit(), () => false,
                (link, target) =>
                {
                    junctions.Add((link, target));
                    return true;
                });

            int created = await service.MaterializeDirectoryLinksAsync(dir, ["Art", "rel-link", "plain.txt", "missing"],
                TestContext.Current.CancellationToken);

            Assert.Equal(2, created);
            Assert.Equal(
            [
                (Path.GetFullPath(art), Path.GetFullPath(real)),
                (Path.GetFullPath(rel), Path.GetFullPath(real)),
            ], junctions);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>junction 创建失败（creator 返回 false）时，把原普通文件写回——不能先删后建失败让该路径凭空消失</summary>
    [Fact]
    public async Task UnsupportedPlatform_JunctionFails_RestoresThePlainFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wt-junc-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string real = Path.Combine(dir, "real");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "x.txt"), "x");
            string art = Path.Combine(dir, "Art");
            File.WriteAllText(art, real); //普通文件形态的链接：内容指向真实存在的目录

            WorktreeGitService service = new(new FakeGit(), () => false, (_, _) => false); //junction 恒失败

            int created = await service.MaterializeDirectoryLinksAsync(dir, ["Art"],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, created);
            Assert.True(File.Exists(art), "失败后普通文件应被还原");
            Assert.Equal(real, File.ReadAllText(art));
            Assert.False(Directory.Exists(art), "失败后不应留下一个 junction 目录");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>junction 创建器抛异常时同样写回原普通文件（与返回 false 同口径，不拖累整体）</summary>
    [Fact]
    public async Task UnsupportedPlatform_JunctionThrows_RestoresThePlainFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wt-junc-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string real = Path.Combine(dir, "real");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "x.txt"), "x");
            string art = Path.Combine(dir, "Art");
            File.WriteAllText(art, real);

            WorktreeGitService service = new(new FakeGit(), () => false,
                (_, _) => throw new IOException("boom"));

            int created = await service.MaterializeDirectoryLinksAsync(dir, ["Art"],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, created);
            Assert.True(File.Exists(art), "抛异常后普通文件应被还原");
            Assert.Equal(real, File.ReadAllText(art));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>文件工具解析器带上 worktreesRoot 时，$WORKTREES/… 简写可以直接当 Read/Edit 的参数（与提示词教的同一条口径）</summary>
    [Fact]
    public void FileToolResolver_WithWorktreesRoot_ResolvesTheShorthand()
    {
        string wtRoot = WorktreeLayout.GetWorktreesRoot(Workspace);
        AgentPathResolver paths = new(Workspace, worktreesRoot: wtRoot);

        Assert.True(paths.TryResolve("$WORKTREES/feature/x", out string full));
        Assert.Equal(Path.Combine(wtRoot, "feature", "x"), full);
    }

    /// <summary>Git Bash 风格的目标路径（/f/…）要转成盘符，否则 Windows 的 .NET 会解析到当前盘根</summary>
    [Fact]
    public void ConvertGitBashDrivePath_ConvertsDriveStyleOnly()
    {
        Assert.Equal(@"F:\MyProject\MyStory\Res\game",
            WorktreeGitService.ConvertGitBashDrivePath("/f/MyProject/MyStory/Res/game"));
        Assert.Equal(@"D:\a b", WorktreeGitService.ConvertGitBashDrivePath("/d/a b"));
        Assert.Null(WorktreeGitService.ConvertGitBashDrivePath("../Res/game")); //相对路径不动
        Assert.Null(WorktreeGitService.ConvertGitBashDrivePath("/tmp/x")); //第二段不是盘符
        Assert.Null(WorktreeGitService.ConvertGitBashDrivePath("C:/abs/x")); //已是盘符
    }

    //================= sync：主工作区 → 已登记 worktree =================
    /// <summary>git worktree：父仓库快进到主工作区分支（共享 .git，本地未推送提交也带过来），
    /// 子模块从主工作区本地 fetch 后快进</summary>
    [Fact]
    public async Task Sync_GitWorktree_MergesParentAndSubmodules()
    {
        string ws = Path.Combine(Path.GetTempPath(), "wrk-sync-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(ws, "src", "Sub"));
        File.WriteAllText(Path.Combine(ws, "src", "Sub", ".git"), "gitdir: fake");
        string root = Path.Combine(Path.GetTempPath(), "wt-sync-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "src", "Sub"));
        File.WriteAllText(Path.Combine(root, "src", "Sub", ".git"), "gitdir: fake");
        try
        {
            ChatSession session = new() { IsTransient = true, WorkspacePath = ws };
            session.AddSessionApprovedWorktreeRoot(root);
            FakeGit git = new()
            {
                Handler = (dir, args) => args switch
                {
                    ["rev-parse", "--git-common-dir"] when
                        dir.Equals(root, StringComparison.OrdinalIgnoreCase) =>
                        new WorktreeGitResult(".git", string.Empty, 0),
                    ["branch", "--show-current"] when dir.EndsWith(Path.Combine(ws, "src") + "/Sub",
                        StringComparison.OrdinalIgnoreCase) => new WorktreeGitResult("master", string.Empty, 0),
                    ["branch", "--show-current"] when dir.Equals(ws, StringComparison.OrdinalIgnoreCase) =>
                        new WorktreeGitResult("main", string.Empty, 0),
                    ["config", "-z", "--file", ".gitmodules", "--get-regexp", _] =>
                        new WorktreeGitResult("submodule.src/Sub.path\u0000src/Sub\u0000", string.Empty, 0),
                    ["merge", "--ff-only", "main"] => new WorktreeGitResult(string.Empty, string.Empty, 0),
                    ["fetch", _, "master"] => new WorktreeGitResult(string.Empty, string.Empty, 0),
                    ["merge", "--ff-only", "FETCH_HEAD"] => new WorktreeGitResult(string.Empty, string.Empty, 0),
                    _ => null,
                },
            };

            string result = await new WorktreeGitService(git).SyncAsync(session, null,
                TestContext.Current.CancellationToken);

            Assert.Contains("已快进到主工作区分支 main", result);
            Assert.Contains("子模块 src/Sub：已快进", result);
            // fetch 直接指向主工作区子模块目录（本地路径），不依赖 origin
            Assert.Contains(git.Commands, c => c.StartsWith($"fetch {Path.Combine(ws, "src") + "/Sub"}",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(ws)) Directory.Delete(ws, recursive: true);
        }
    }

    /// <summary>同步只对已登记的 worktree 生效：没登记过的 path 报错</summary>
    [Fact]
    public async Task Sync_UnregisteredPath_ReturnsError()
    {
        string result = await new WorktreeGitService(new FakeGit()).SyncAsync(NewSession(), "/tmp/wt/nope",
            TestContext.Current.CancellationToken);

        Assert.Contains("没有登记过", result);
    }

    /// <summary>没有登记的 worktree：提示先 create/set</summary>
    [Fact]
    public async Task Sync_NoRoots_ReturnsError()
    {
        string result = await new WorktreeGitService(new FakeGit()).SyncAsync(NewSession(), null,
            TestContext.Current.CancellationToken);

        Assert.Contains("没有登记的 worktree", result);
    }

    /// <summary>纯副本：把主工作区内容重新复制过去，覆盖已有文件</summary>
    [Fact]
    public async Task Sync_PlainCopy_RerunsCopyOverExisting()
    {
        string ws = Path.Combine(Path.GetTempPath(), "wrk-sync-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ws);
        File.WriteAllText(Path.Combine(ws, "a.txt"), "new");
        string root = Path.Combine(Path.GetTempPath(), "wt-sync-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "old");
        try
        {
            ChatSession session = new() { IsTransient = true, WorkspacePath = ws };
            session.AddSessionApprovedWorktreeRoot(root);
            FakeGit git = new()
            {
                Handler = (_, args) => args is ["rev-parse", "--git-common-dir"]
                    ? new WorktreeGitResult(string.Empty, "not a git repository", 128)
                    : null,
            };

            string result = await new WorktreeGitService(git).SyncAsync(session, null,
                TestContext.Current.CancellationToken);

            Assert.Contains("已从主工作区重新复制", result);
            Assert.Equal("new", File.ReadAllText(Path.Combine(root, "a.txt"))); // 覆盖了旧内容
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(ws)) Directory.Delete(ws, recursive: true);
        }
    }

    //================= 审批动态豁免 =================

    /// <summary>登记后 worktree 内写入免审批；取消后立即恢复要审批——同一规则集，无需重建装配</summary>
    [Fact]
    public async Task Approval_WorktreeRoots_ApplyDynamicallyWithoutRebuild()
    {
        List<ChatSession.SessionWorktreeGrant> roots = [];
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, Workspace,
                sessionWorktreeRootsSource: () => roots);
        string inside = Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x/src/A.cs");
        var grant = new ChatSession.SessionWorktreeGrant(Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x"),
            Path.GetFullPath(Workspace));

        // 未登记：工作区外写入要审批
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, Edit(inside)));

        // 登记：免审批（动态生效）
        roots.Add(grant);
        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(rules, Edit(inside)));

        // 切回：恢复要审批（动态生效）
        roots.Clear();
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, Edit(inside)));

        // 别的工作区外路径不受牵连
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, Edit("/tmp/elsewhere/b.cs")));
    }

    /// <summary>
    /// 审批层的路径口径与执行层同源：模型写 $WORKTREES/… 时，执行落在树里、审批也认界内。
    /// 两边一旦分家，就是“写进去了但每次都要点审批”
    /// </summary>
    [Fact]
    public async Task Approval_WorktreesShorthand_ResolvesLikeTheToolLayer()
    {
        string worktreesRoot = Path.GetFullPath("/tmp/uiharu-wt-root");
        string root = Path.Combine(worktreesRoot, "feature", "x");
        List<ChatSession.SessionWorktreeGrant> roots = [new(root, Path.GetFullPath(Workspace))];
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, Workspace,
                sessionWorktreeRootsSource: () => roots, worktreesRoot: worktreesRoot);

        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(rules, Edit("$WORKTREES/feature/x/src/A.cs")));
        // 没传 worktree 根的老口径：简写解析不了，当界外，保守地要审批
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> legacy =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, Workspace,
                sessionWorktreeRootsSource: () => roots);
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(legacy, Edit("$WORKTREES/feature/x/src/A.cs")));
    }

    /// <summary>登记带工作区：换绑工作区/群换工作区后旧登记自动失效（不必逐会话清理）</summary>
    [Fact]
    public async Task Approval_GrantFromOtherWorkspace_IsNotExempt()
    {
        List<ChatSession.SessionWorktreeGrant> roots =
        [
            new(Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x"),
                Path.GetFullPath("/tmp/old-workspace")),
        ];
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, Workspace,
                sessionWorktreeRootsSource: () => roots);

        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules,
            Edit(Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x/src/A.cs"))));
    }

    /// <summary>只读档不因登记放行写入（与产出房间同一口径）；FullAuto 档也认登记</summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly, false)]
    [InlineData(EAgentPermissionMode.AutoEdit, true)]
    [InlineData(EAgentPermissionMode.FullAuto, true)]
    public async Task Approval_WorktreeRoots_ModeAware(EAgentPermissionMode mode, bool expected)
    {
        List<ChatSession.SessionWorktreeGrant> roots =
        [
            new(Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x"), Path.GetFullPath(Workspace)),
        ];
        List<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>> rules =
            ApprovalModeMapper.BuildRules(mode, Workspace, sessionWorktreeRootsSource: () => roots);

        Assert.Equal(expected, await ApprovalRuleProbe.IsApprovedAsync(rules,
            Edit(Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x/src/A.cs"))));
    }

    /// <summary>化身判定（离席替派活者点）与正常放行同一份口径：派活者登记的 worktree 算界内</summary>
    [Fact]
    public void AvatarJudge_RecognizesRequesterWorktreeRoots()
    {
        ChatSession requester = NewSession();
        requester.AddSessionApprovedWorktreeRoot("/tmp/uiharu-ws-worktrees/feature/x");

        Assert.False(ApprovalModeMapper.IsOutOfWorkspaceWrite(
            Edit(Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x/src/A.cs")),
            AgentBuildProfile.PathResolverOf(requester), requester.SnapshotSessionApprovedWorktreeRoots()));
        Assert.True(ApprovalModeMapper.IsOutOfWorkspaceWrite(
            Edit("/tmp/elsewhere/b.cs"),
            AgentBuildProfile.PathResolverOf(requester), requester.SnapshotSessionApprovedWorktreeRoots()));
    }

    /// <summary>不认登记的老入口照旧：没传 worktree 根的化身判定按工作区外算</summary>
    [Fact]
    public void AvatarJudge_WithoutRoots_StaysConservative()
    {
        ChatSession requester = NewSession();
        requester.AddSessionApprovedWorktreeRoot("/tmp/uiharu-ws-worktrees/feature/x");

        Assert.True(ApprovalModeMapper.IsOutOfWorkspaceWrite(
            Edit(Path.GetFullPath("/tmp/uiharu-ws-worktrees/feature/x/src/A.cs")),
            AgentBuildProfile.PathResolverOf(requester)));
    }
}
