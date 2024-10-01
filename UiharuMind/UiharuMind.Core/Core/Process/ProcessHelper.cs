using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using CliWrap;
using CliWrap.EventStream;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Process;

public static class ProcessHelper
{
    private static ConcurrentBag<int> _processIds = new();

    public static void CancelAllProcesses()
    {
        // 并行终止所有进程
        var tasks = _processIds.Select(processId => Task.Run(() =>
        {
            // 尝试获取进程
            using var process = FindProcessById(processId);
            if (process == null) return;
            // 强行终止进程
            process.Kill();
            process.WaitForExit(); // 等待进程完全退出
            Console.WriteLine($"Process {processId} terminated.");
        })).ToList();

        // 等待所有任务完成
        Task.WhenAll(tasks).Wait();

        // 清空任务列表
        _processIds.Clear();
    }

    /// <summary>
    /// 强行清理本地运行的服务
    /// </summary>
    public static void ForceClearAllProcesses()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(LLamaCppSettingConfig.ServerExeName))
        {
            if (!_processIds.Contains(process.Id)) process.Kill();
        }
    }

    private static System.Diagnostics.Process? FindProcessById(int processId)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById(processId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 安全终止 CancellationTokenSource
    /// </summary>
    /// <param name="cts"></param>
    public static void SafeStop(this CancellationTokenSource? cts)
    {
        if (cts == null) return;
        if (cts.IsCancellationRequested) return;
        cts.Cancel();
    }

    /// <summary>
    /// 执行指定目录下的某个可执行文件
    /// </summary>
    /// <param name="dirPath"></param>
    /// <param name="exeName"></param>
    /// <param name="args"></param>
    /// <param name="onLogCallback"></param>
    /// <param name="token"></param>
    public static async Task StartProcess(string dirPath, string exeName, string args,
        Action<string>? onLogCallback = null, CancellationToken token = default)
    {
        await StartProcess(Path.Combine(dirPath, exeName), args, onLogCallback, token);
    }

// /// <summary>
// /// 执行指定可执行文件
// /// </summary>
// /// <param name="exePath"></param>
// /// <param name="args"></param>
// /// <param name="onLogCallback"></param>
// public static async Task StartProcess(string exePath, string args,
//     Action<string>? onLogCallback, CancellationToken token)
// {
//     await StartProcess(exePath, args, onLogCallback,token);
// }

//     /// <summary>
//     /// 执行指定可执行文件
//     /// </summary>
//     /// <param name="exePath"></param>
//     /// <param name="args"></param>
//     /// <param name="onLogCallback"></param>
//     /// <param name="token"></param>
//     public static async Task StartProcess(string? exePath, string args, Action<string>? onLogCallback = null,
//         CancellationToken token = default)
//     {
//         if (exePath == null)
//         {
//             Log.Error("No executable path specified.");
//             return;
//         }
//
//         var cmd = Cli.Wrap(exePath).WithArguments(args).WithValidation(CommandResultValidation.None);
//         string lastLine = "";
//
//         try
//         {
//             if (onLogCallback == null)
//             {
//                 var result = await cmd.ExecuteAsync(token).ConfigureAwait(false);
//                 if (result.ExitCode == 0)
//                 {
//                     Log.Debug($"Process {exePath} executed successfully.");
//                 }
//                 else
//                 {
//                     Log.Error($"Process {exePath} executed failed with exit code {result.ExitCode}.");
//                 }
//
//                 return;
//             }
//
//             // 创建自定义的 PipeTarget
//             // var stdOutBuffer = new StringBuilder();
//             // var stdOutPipe = new CallbackPipeTarget(stdOutBuffer,
//             //     (data) => { Console.WriteLine($"StdOut modified: {data}"); });
//             //
//             // var stdErrBuffer = new StringBuilder();
//             // var stdErrPipe = new CallbackPipeTarget(stdErrBuffer,
//             //     (data) => { Console.WriteLine($"StdErr modified: {data}"); });
//
//             cmd.WithStandardOutputPipe(PipeTarget.ToDelegate((x) => { Console.WriteLine($"StdOut modified: {x}"); }))
//                 .WithStandardErrorPipe(PipeTarget.ToDelegate((x) => { Console.WriteLine($"StdErr modified: {x}"); }));
//          
//       
//             var task = cmd.ExecuteAsync(token);
// cmd.ListenAsync
//             var compelxResult = await task;
//             if (compelxResult.ExitCode != 0)
//             {
//                 Log.Error(
//                     $"Process {exePath} exited with code {compelxResult.ExitCode}. Last log line: {lastLine}");
//             }
//             // var processId = task.ProcessId;
//             // await foreach (var cmdEvent in cmd.ListenAsync(token).ConfigureAwait(false))
//             // {
//             //     switch (cmdEvent)
//             //     {
//             //         case StandardOutputCommandEvent stdOut:
//             //             lastLine = stdOut.Text;
//             //             onLogCallback.Invoke(lastLine);
//             //             break;
//             //         case StandardErrorCommandEvent stdErr:
//             //             lastLine = stdErr.Text;
//             //             onLogCallback.Invoke(lastLine);
//             //             break;
//             //         case ExitedCommandEvent exited:
//             //             if (exited.ExitCode != 0)
//             //             {
//             //                 Log.Error(
//             //                     $"Process {exePath} exited with code {exited.ExitCode}. Last log line: {lastLine}");
//             //             }
//             //
//             //             break;
//             //     }
//             // }
//         }
//         catch (OperationCanceledException)
//         {
//         }
//         catch (Exception ex)
//         {
//             Log.Error($"Execution error: {ex.Message}. Last log line: {lastLine}");
//         }
//     }

    /// <summary>
    /// 执行指定可执行文件
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="args"></param>
    /// <param name="onLogCallback"></param>
    /// <param name="token"></param>
    public static async Task StartProcess(string? exePath, string args, Action<string>? onLogCallback = null,
        CancellationToken token = default)
    {
        if (exePath == null)
        {
            Log.Error("No executable path specified.");
            return;
        }

        var cmd = Cli.Wrap(exePath).WithArguments(args).WithValidation(CommandResultValidation.None);
        string lastLine = "";

        int processId = 0;

        try
        {
            if (onLogCallback == null)
            {
                var task = cmd.ExecuteAsync(token);
                processId = task.ProcessId;
                _processIds.Add(processId);
                var result = await task.ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    Log.Debug($"Process {exePath} executed successfully.");
                }
                else
                {
                    Log.Error($"Process {exePath} executed failed with exit code {result.ExitCode}.");
                }

                return;
            }

            await foreach (var cmdEvent in cmd.ListenAsync(token).ConfigureAwait(false))
            {
                switch (cmdEvent)
                {
                    case StartedCommandEvent cmdStarted:
                        processId = cmdStarted.ProcessId;
                        _processIds.Add(processId);

                        // Log.Debug($"Process {exePath} started with PID {processId}.");
                        break;
                    case StandardOutputCommandEvent stdOut:
                        lastLine = stdOut.Text;
                        onLogCallback.Invoke(lastLine);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        lastLine = stdErr.Text;
                        onLogCallback.Invoke(lastLine);
                        break;
                    case ExitedCommandEvent exited:
                        if (exited.ExitCode != 0)
                        {
                            Log.Error(
                                $"Process {exePath} exited with code {exited.ExitCode}. Last log line: {lastLine}");
                        }

                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Execution error: {ex.Message}. Last log line: {lastLine}");
        }
        finally
        {
            _processIds.TryTake(out processId);
        }
    }
}