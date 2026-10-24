/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution.Tools.Worktree;

/// <summary>
/// worktree 扩展背后的四把工具：create / set / reset / status。
/// 命令名与原工具名的对照只写在 <see cref="Extensions.BuiltinExtensions"/>，
/// 这里只负责造工具。命令执行在 <see cref="WorktreeGitService"/>，git 在
/// <see cref="IWorktreeGitRunner"/>——都是可注入的，测试用假 git 跑逻辑
/// </summary>
public static class WorktreeTools
{
    /// <summary>create：一键创建并登记</summary>
    public const string CreateToolName = "WorktreeCreate";

    /// <summary>set：登记自己已有的 worktree</summary>
    public const string SetToolName = "WorktreeSet";

    /// <summary>list：列出已登记 worktree（分支/上次修改时间/未提交改动）</summary>
    public const string ListToolName = "WorktreeList";

    /// <summary>status：查询当前状态</summary>
    public const string StatusToolName = "WorktreeStatus";

    /// <summary>reset：取消登记（动态切回仅工作目录免审批）。已临时注释待删（2026-10-24）</summary>
    public const string ResetToolName = "WorktreeReset";

    /// <summary>remove：删除并取消登记（清理）</summary>
    public const string RemoveToolName = "WorktreeRemove";

    /// <summary>sync：把主工作区最新状态同步进已登记的 worktree</summary>
    public const string SyncToolName = "WorktreeSync";

    /// <summary>
    /// 造六把工具（reset 已临时注释待删）
    /// </summary>
    /// <param name="sessionSource">当前会话（写登记与读工作目录）；取不到为 null 时报错</param>
    /// <param name="git">git 执行器；缺省用系统 git</param>
    /// <returns>六把工具，顺序 create/set/sync/remove/list/status</returns>
    public static IReadOnlyList<AITool> Create(Func<ChatSession?> sessionSource, IWorktreeGitRunner? git = null)
    {
        git ??= GitWorktreeRunner.Shared;
        WorktreeGitService service = new(git);

        ChatSession RequireSession()
        {
            return sessionSource?.Invoke() ?? throw new InvalidOperationException("会话不可用，无法读写 worktree 登记。");
        }

        return
        [
            ToolJson.CreateFunction(
                async ([Description("worktree 目录名，同时默认作新分支名；可含斜杠（如 feature/foo）")]
                        string name,
                    [Description("基于哪个分支/提交创建；缺省用当前 HEAD。只写分支名即可，会自动认 origin/ 上同名的")]
                        string? baseBranch = null,
                    [Description("新分支名；缺省同 name")]
                        string? branch = null,
                    [Description("worktree 落盘目录的绝对路径（相对路径按工作目录解析）；缺省为数据目录项目共享的 Worktrees 下；" +
                                      "目标须为空或不存在")]
                        string? directory = null,
                    CancellationToken cancellationToken = default) =>
                    await service.CreateAsync(RequireSession(), name, baseBranch, branch, directory, cancellationToken)
                        .ConfigureAwait(false),
                CreateToolName,
                "创建一个隔离的 worktree（工作副本）并自动登记为免审批——登记后该 worktree 内的文件写入不再问（AutoEdit/FullAuto 档）。" +
                "工作目录是 git 仓库时：自动拉取（fetch origin）、初始化子模块（正常 git 拉取，拉不下来的从主工作区复制补齐，" +
                "带 git 身份的子模块在目标 commit 上建同名分支）、按平台能力修复软链接（Windows 需开发者模式/管理员），" +
                "同名分支已存在则直接检出；不是 git 仓库时：复制工作目录内容成隔离副本（跳过 node_modules 等常见大目录）。" +
                "落盘默认数据目录项目共享的 Worktrees 下（directory 给绝对路径即落在别处，目标须为空/不存在）。" +
                "本工具不动会话的工作目录（相对路径仍解析到原工作目录），worktree 内请用绝对路径。"),
            ToolJson.CreateFunction(
                async ([Description("worktree 根目录的绝对路径（可给树内子目录，会归到树根）。必须属于会话绑定的同一仓库")]
                        string path,
                    CancellationToken cancellationToken = default) =>
                    await service.SetAsync(RequireSession(), path, cancellationToken).ConfigureAwait(false),
                SetToolName,
                "登记用户自己已有的隔离工作副本：登记后该目录内的文件写入免审批（AutoEdit/FullAuto 档），" +
                "子代理派活时自动继承。git 仓库时校验目标属于同一仓库；不是 git 仓库时登记你准备的副本（确认目录是你的）。" +
                "不动工作目录与草稿目录。"),
            ToolJson.CreateFunction(
                async ([Description("要同步的 worktree 根目录的绝对路径；不填同步该会话全部已登记的")]
                        string? path = null,
                    CancellationToken cancellationToken = default) =>
                    await service.SyncAsync(RequireSession(), path, cancellationToken).ConfigureAwait(false),
                SyncToolName,
                "把主工作区的最新状态同步进已登记的 worktree（方向：主工作区 → worktree）：" +
                "git worktree 的父仓库在 worktree 里 merge --ff-only 到主工作区当前分支" +
                "（所有 worktree 共享 .git，主工作区未推送的本地提交也会带过来，不联网）；" +
                "子模块从主工作区子模块目录本地 fetch 后快进；纯副本重新复制内容覆盖。" +
                "只对已登记的 worktree 生效；有分叉/冲突/未提交改动照实报告，不强制、不回滚。" +
                "不动会话的工作目录。"),
            // reset 已临时注释（2026-10-24）：模型看到它以为能真正「切回」主工作目录，实际做不到动态换目录，
            // 只是取消豁免——留着会让模型误会。底层 WorktreeGitService.Reset 与测试保留，恢复时把下面三行放出来。
            // ToolJson.CreateFunction(
            //     ([Description("要取消登记的 worktree 根；不填取消全部")]
            //         string? path = null) => WorktreeGitService.Reset(RequireSession(), path),
            //     ResetToolName,
            //     "取消登记本会话的 worktree（动态切回）：取消后该目录内的写入恢复要审批。" +
            //     "只给 path 就只取消那一个，不给就全部取消。只动会话登记，不改盘、不动工作目录；" +
            //     "切回后结果会提示清理（worktree.remove）。"),
            ToolJson.CreateFunction(
                async ([Description("要删除的 worktree 根目录的绝对路径（须已登记）")]
                        string path,
                    [Description("强制删除：丢弃该 worktree 里的未提交改动。默认 false（git 会拒绝删除有改动的树）")]
                        bool force = false,
                    CancellationToken cancellationToken = default) =>
                    await service.RemoveAsync(RequireSession(), path, force, cancellationToken).ConfigureAwait(false),
                RemoveToolName,
                "删除并取消登记一个已登记的 worktree（git worktree remove）。" +
                "有未提交改动时 git 默认拒删；确认丢弃后传 force=true。清理不用的 worktree 用它。"),
            ToolJson.CreateFunction(
                async (CancellationToken cancellationToken = default) =>
                    await service.ListAsync(RequireSession(), cancellationToken).ConfigureAwait(false),
                ListToolName,
                "列出本会话已登记的 worktree：每个的分支、上次修改时间（上次提交时间，无提交时回落到目录改动时间）" +
                "与有无未提交改动。只读。"),
            ToolJson.CreateFunction(
                () => WorktreeGitService.Status(RequireSession()),
                StatusToolName,
                "查询本会话的 worktree 状态：工作目录（相对路径基准）、已登记的 worktree、草稿目录与记忆目录。只读。"),
        ];
    }
}
