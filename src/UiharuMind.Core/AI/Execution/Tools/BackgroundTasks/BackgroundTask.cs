/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics;
using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 一个在跑的后台任务：自己起的进程，输出逐行写进日志文件。
///
/// 不经 Shell 工具的执行器：那边一次调用要等输出管道关闭才返回，时限一到还会结束整棵进程树——
/// 这两件事正是长任务要躲开的。这里只认进程本身退出，输出由我们自己读走落盘
/// </summary>
public sealed class BackgroundTask
{
    // 进程退出后等输出读完的最长时间:它放到后台的子进程可能一直占着管道,不能陪着等
    private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2);

    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly TaskCompletionSource _stdoutClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stderrClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _timeLimit = new();
    private int _end = -1; //主动结束时先记下原因,退出回调据此区分

    /// <summary>任务编号</summary>
    public string Id { get; }

    /// <summary>启动它的会话标识，结果送回这里</summary>
    public string OwnerSessionId { get; }

    /// <summary>命令原文</summary>
    public string Command { get; }

    /// <summary>一句话说明，显示给用户</summary>
    public string Description { get; }

    /// <summary>日志文件的绝对路径</summary>
    public string LogPath { get; }

    /// <summary>时间上限</summary>
    public TimeSpan MaxRuntime { get; }

    /// <summary>启动时刻</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    /// <summary>结束时完成，给出结局</summary>
    public Task<BackgroundTaskOutcome> Completion { get; private set; } = null!;

    private BackgroundTask(string ownerSessionId, string command, string description, TimeSpan maxRuntime,
        BackgroundTaskLaunch launch)
    {
        Id = Guid.NewGuid().ToString("N")[..8];
        OwnerSessionId = ownerSessionId;
        Command = command;
        Description = description;
        MaxRuntime = maxRuntime;

        Directory.CreateDirectory(launch.LogDirectory);
        LogPath = Path.Combine(launch.LogDirectory, $"{Id}.log");
        _log = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false)) { AutoFlush = true };

        _process = new Process { StartInfo = BuildStartInfo(launch, command), EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data, _stdoutClosed);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data, _stderrClosed);
        _process.Exited += (_, _) => _exited.TrySetResult();
    }

    /// <summary>
    /// 起一个后台任务。进程起不来时抛出（日志文件已建，留着无害）
    /// </summary>
    /// <param name="ownerSessionId">启动它的会话标识</param>
    /// <param name="command">命令原文</param>
    /// <param name="description">一句话说明</param>
    /// <param name="maxRuntime">时间上限</param>
    /// <param name="launch">起跑环境</param>
    /// <returns>在跑的任务</returns>
    internal static BackgroundTask Start(string ownerSessionId, string command, string description,
        TimeSpan maxRuntime, BackgroundTaskLaunch launch)
    {
        BackgroundTask task = new(ownerSessionId, command, description, maxRuntime, launch);
        try
        {
            task._process.Start();
        }
        catch
        {
            task._log.Dispose();
            task._process.Dispose();
            task._timeLimit.Dispose();
            throw;
        }

        task.Completion = task.WatchAsync();
        task._process.BeginOutputReadLine();
        task._process.BeginErrorReadLine();
        _ = task.EnforceTimeLimitAsync();
        return task;
    }

    /// <summary>
    /// 主动结束整棵进程树。已经退出的不受影响（收尾窗口里点停止不能把自然退出记成叫停），先到的原因为准。
    ///
    /// 管不到的：命令自己用 <c>&amp;</c>/<c>nohup</c> 丢出去、父进程又先退了的孙进程，它已被系统收养、不在树上
    /// </summary>
    /// <param name="reason">结束原因</param>
    internal void Kill(EBackgroundTaskEnd reason)
    {
        if (_exited.Task.IsCompleted) return;
        if (Interlocked.CompareExchange(ref _end, (int)reason, -1) != -1) return;
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception e)
        {
            // 已经退出(InvalidOperation/Win32),或树里有杀不掉的(如 sudo 起的 root 进程,抛 AggregateException)。
            // 尽力而为:停止按钮与退出收尾都不能因为这里炸掉
            Log.Warning($"Kill background task tree failed: id={Id}: {e.Message}");
        }
    }

    private static ProcessStartInfo BuildStartInfo(BackgroundTaskLaunch launch, string command)
    {
        ProcessStartInfo info = new()
        {
            FileName = launch.ShellBinary,
            WorkingDirectory = launch.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in launch.ArgumentsFor(command)) info.ArgumentList.Add(argument);
        foreach ((string name, string? value) in launch.Environment)
        {
            if (value == null) info.Environment.Remove(name);
            else info.Environment[name] = value;
        }

        if (launch.IsPowerShell) info.Environment["PSDefaultParameterValues"] = "Out-File:Encoding=utf8";
        return info;
    }

    private void OnLine(string? line, TaskCompletionSource closed)
    {
        if (line == null)
        {
            closed.TrySetResult();
            return;
        }

        lock (_log)
        {
            try
            {
                _log.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
                //收尾后才到的残余输出,丢掉
            }
        }
    }

    private async Task EnforceTimeLimitAsync()
    {
        try
        {
            await Task.Delay(MaxRuntime, _timeLimit.Token).ConfigureAwait(false);
            Kill(EBackgroundTaskEnd.TimeLimit);
        }
        catch (OperationCanceledException)
        {
            //先退出了
        }
    }

    private async Task<BackgroundTaskOutcome> WatchAsync()
    {
        await _exited.Task.ConfigureAwait(false);
        _timeLimit.Cancel();
        await Task.WhenAny(Task.WhenAll(_stdoutClosed.Task, _stderrClosed.Task), Task.Delay(DrainLimit))
            .ConfigureAwait(false);

        int? exitCode = null;
        try
        {
            exitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            //被结束的进程有时取不到
        }

        TimeSpan duration = DateTimeOffset.Now - StartedAt;
        lock (_log) _log.Dispose();
        _process.Dispose();
        _timeLimit.Dispose();

        EBackgroundTaskEnd end = _end < 0 ? EBackgroundTaskEnd.Exited : (EBackgroundTaskEnd)_end;
        Log.Debug($"Background task ended: id={Id} session={OwnerSessionId} end={end} exit={exitCode} "
                  + $"duration={duration.TotalSeconds:0}s");
        return new BackgroundTaskOutcome(this, end, exitCode, duration);
    }
}
