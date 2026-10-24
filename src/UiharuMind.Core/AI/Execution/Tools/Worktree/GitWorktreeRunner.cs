/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics;

namespace UiharuMind.Core.AI.Execution.Tools.Worktree;

/// <summary>
/// 用系统 git 实现 <see cref="IWorktreeGitRunner"/>。stateless：每次调用起一个新进程，
/// 不保留 shell 状态；<c>GIT_TERMINAL_PROMPT=0</c> 让凭据缺失时直接失败而不是卡在交互提示上
/// </summary>
public sealed class GitWorktreeRunner : IWorktreeGitRunner
{
    /// <summary>共享实例（无状态，可复用）</summary>
    public static readonly GitWorktreeRunner Shared = new();

    /// <summary>单条命令的上限。fetch 大仓库可能慢，十分钟盖得住正常网络</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    public async Task<WorktreeGitResult> RunAsync(string workingDirectory, IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        Process process = new();
        try
        {
            process.StartInfo.FileName = "git";
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(workingDirectory);
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);

            try
            {
                process.Start();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return new WorktreeGitResult(string.Empty, $"无法启动 git：{e.Message}", -1);
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                // 输出读取任务在取消时可能也已失败：吞掉，避免未观察的任务异常；流读取器随进程释放自行收尾
                try
                {
                    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                return new WorktreeGitResult(string.Empty,
                    cancellationToken.IsCancellationRequested ? "命令被取消。" : $"git 超时（>{CommandTimeout.TotalMinutes:0} 分钟）。",
                    -1);
            }

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);
            return new WorktreeGitResult(output, error, process.ExitCode);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 进程已经结束，忽略
        }
    }
}
