/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Worktree;

/// <summary>
/// 一条 git 命令的执行结果
/// </summary>
/// <param name="Output">标准输出</param>
/// <param name="Error">标准错误</param>
/// <param name="ExitCode">退出码</param>
public sealed record WorktreeGitResult(string Output, string Error, int ExitCode)
{
    /// <summary>退出码为 0</summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// git 命令执行器：worktree 工具与真实 git 的边界。<b>只负责跑命令、收输出</b>，
/// 命令序列与结果解读在 <see cref="WorktreeGitService"/>——那里才是可测的逻辑
/// </summary>
public interface IWorktreeGitRunner
{
    /// <summary>
    /// 在指定工作目录执行 <c>git &lt;args…&gt;</c>
    /// </summary>
    /// <param name="workingDirectory">git 的工作目录（<c>-C</c> 指向它）</param>
    /// <param name="args">参数（不含 git 本身与 -C）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>结果；超时/取消按失败返回</returns>
    Task<WorktreeGitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args,
        CancellationToken cancellationToken);
}
