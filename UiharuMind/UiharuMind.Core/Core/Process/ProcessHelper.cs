using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Process;

public static class ProcessHelper
{
    /// <summary>
    /// 执行指定目录下的某个可执行文件
    /// </summary>
    /// <param name="dirPath"></param>
    /// <param name="exeName"></param>
    /// <param name="args"></param>
    /// <param name="onInitCallback"></param>
    /// <param name="onLogCallback"></param>
    public static async Task StartProcess(string dirPath, string exeName, string args,
        Action<CancellationTokenSource>? onInitCallback = null,
        Action<string, CancellationTokenSource>? onLogCallback = null)
    {
        await StartProcess(Path.Combine(dirPath, exeName), args, onInitCallback, onLogCallback);
    }

    // /// <summary>
    // /// 执行指定可执行文件，允许通过异步取消操作
    // /// </summary>
    // /// <param name="exePath"></param>
    // /// <param name="args"></param>
    // /// <param name="callback"></param>
    // public static async Task StartProcess(string exePath, string args,
    //     Func<string, CancellationTokenSource, Task>? callback)
    // {
    //     using var cts = new CancellationTokenSource();
    //     var cmd = Cli.Wrap(exePath).WithArguments(args).WithValidation(CommandResultValidation.None);
    //
    //     try
    //     {
    //         if (callback == null)
    //         {
    //             await cmd.ExecuteAsync();
    //             return;
    //         }
    //
    //         await foreach (var cmdEvent in cmd.ListenAsync(cancellationToken: cts.Token))
    //         {
    //             switch (cmdEvent)
    //             {
    //                 // case StartedCommandEvent started:
    //                 //     Log.Debug($"Process started; ID: {started.ProcessId}");
    //                 //     break;
    //                 case StandardOutputCommandEvent stdOut:
    //                     // Log.Debug($"Out> {stdOut.Text}");
    //                     // if (stdOut.Text.StartsWith("llm_load_tensors")) await cts.CancelAsync();
    //                     // info.UpdateValue(stdOut.Text);
    //                     // await ParseModelInfo(stdOut.Text, info, cts);
    //                     await callback.Invoke(stdOut.Text, cts);
    //                     break;
    //                 case StandardErrorCommandEvent stdErr:
    //                     // Log.Debug($"Err> {stdErr.Text}");
    //                     // if (stdErr.Text.StartsWith("llm_load_tensors")) await cts.CancelAsync();
    //                     // info.UpdateValue(stdErr.Text);
    //                     // await ParseModelInfo(stdErr.Text, info, cts);
    //                     await callback.Invoke(stdErr.Text, cts);
    //                     break;
    //                 case ExitedCommandEvent exited:
    //                     Log.Debug($"Process exited; Code: {exited.ExitCode}");
    //                     break;
    //             }
    //         }
    //     }
    //     catch (OperationCanceledException)
    //     {
    //         // Log.Debug("Scan Canceled");
    //     }
    //
    //     // onEnd?.Invoke();
    //     // return info;
    // }

    /// <summary>
    /// 执行指定可执行文件
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="args"></param>
    /// <param name="onLogCallback"></param>
    public static async Task StartProcess(string exePath, string args,
        Action<string, CancellationTokenSource>? onLogCallback)
    {
        await StartProcess(exePath, args, null, onLogCallback);
    }

    /// <summary>
    /// 执行指定可执行文件
    /// </summary>
    /// <param name="exePath"></param>
    /// <param name="args"></param>
    /// <param name="onInitCallback"></param>
    /// <param name="onLogCallback"></param>
    public static async Task StartProcess(string exePath, string args,
        Action<CancellationTokenSource>? onInitCallback = null,
        Action<string, CancellationTokenSource>? onLogCallback = null)
    {
        using var cts = new CancellationTokenSource();
        var cmd = Cli.Wrap(exePath).WithArguments(args).WithValidation(CommandResultValidation.None);
        try
        {
            onInitCallback?.Invoke(cts);
            if (onLogCallback == null)
            {
                var result = await cmd.ExecuteBufferedAsync();
                if (result.ExitCode == 0)
                {
                    Log.Debug($"Process {exePath} excuted successfully.");
                }
                else
                {
                    Log.Error($"Process {exePath} excuted failed: {result.StandardError}");
                }

                return;
            }

            await foreach (var cmdEvent in cmd.ListenAsync(cancellationToken: cts.Token))
            {
                switch (cmdEvent)
                {
                    // case StartedCommandEvent started:
                    //     Log.Debug($"Process started; ID: {started.ProcessId}");
                    //     break;
                    case StandardOutputCommandEvent stdOut:
                        onLogCallback.Invoke(stdOut.Text, cts);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        onLogCallback.Invoke(stdErr.Text, cts);
                        break;
                    case ExitedCommandEvent exited:
                        Log.Debug($"Process exited; Code: {exited.ExitCode}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Execution error: " + ex.Message);
        }
        finally
        {
            if (!cts.IsCancellationRequested) await cts.CancelAsync();
        }
    }
}